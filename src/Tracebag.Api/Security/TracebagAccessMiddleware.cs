using Tracebag.Api.Auth;

namespace Tracebag.Api.Security;

public sealed class TracebagAccessMiddleware
{
    private readonly RequestDelegate _next;
    private readonly TracebagOptions _options;

    public TracebagAccessMiddleware(RequestDelegate next, TracebagOptions options)
    {
        _next = next;
        _options = options;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_options.AuthEnabled || !context.Request.Path.StartsWithSegments("/api"))
        {
            await _next(context);
            return;
        }

        if (IsAnonymousEndpoint(context.Request.Path))
        {
            await _next(context);
            return;
        }

        if (context.User.Identity?.IsAuthenticated == true)
        {
            await _next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new
        {
            error = "not_authenticated",
            message = "Login required."
        }, context.RequestAborted);
    }

    private static bool IsAnonymousEndpoint(PathString path)
    {
        return path.StartsWithSegments("/api/auth/login")
            || path.StartsWithSegments("/api/auth/me");
    }
}
