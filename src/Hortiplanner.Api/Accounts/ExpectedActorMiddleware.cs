using System.Security.Claims;

namespace Hortiplanner.Api.Accounts;

internal sealed class ExpectedActorMiddleware(RequestDelegate next)
{
    private const string Header = "X-Expected-Actor";

    public async Task InvokeAsync(HttpContext context)
    {
        var method = context.Request.Method;
        if (context.User.Identity?.IsAuthenticated == true
            && (HttpMethods.IsPost(method) || HttpMethods.IsPut(method) || HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method))
            && context.Request.Headers.TryGetValue(Header, out var expectedActor)
            && (expectedActor.Count != 1 || !Guid.TryParse(expectedActor[0], out var expectedId)
                || !Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var actualId) || expectedId != actualId))
        {
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            await context.Response.WriteAsJsonAsync(new { problem = "actor_mismatch" }, context.RequestAborted);
            return;
        }

        await next(context);
    }
}
