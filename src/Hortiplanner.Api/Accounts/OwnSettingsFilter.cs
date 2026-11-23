using Microsoft.AspNetCore.Identity;

namespace Hortiplanner.Api.Accounts;

/// <summary>
/// Account settings available away from a bench and the shared nursery team account.
/// </summary>
internal sealed class OwnSettingsFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var user = context.HttpContext.User;

        if (user.FindFirst(AccountServices.BenchSessionClaim) is not null)
        {
            return TypedResults.Forbid();
        }

        var users = context.HttpContext.RequestServices.GetRequiredService<UserManager<AppUser>>();

        if (await users.GetUserAsync(user) is { IsTeamAccount: true })
        {
            return TypedResults.Forbid();
        }

        return await next(context);
    }
}
