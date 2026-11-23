using System.Security.Claims;
using Microsoft.AspNetCore.Identity;

namespace Hortiplanner.Api.Accounts;

/// <summary>Changing credentials requires a recent check of a personal sign-in method.</summary>
internal sealed class PersonalCredentialFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var user = context.HttpContext.User;
        var services = context.HttpContext.RequestServices;
        var clock = services.GetRequiredService<TimeProvider>();
        var signIn = services.GetRequiredService<SignInManager<AppUser>>();

        if (!SignInRules.IsPersonal(user)
            || !SignInRules.IsRecentCredentialCheck(user.FindFirstValue(AccountServices.CredentialCheckedAtClaim), clock)
            || await signIn.ValidateSecurityStampAsync(user) is not { ArchivedAt: null, IsTeamAccount: false })
        {
            return TypedResults.Forbid();
        }

        return await next(context);
    }
}
