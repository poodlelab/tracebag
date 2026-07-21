using Tracebag.Api.Auth;

namespace Tracebag.Api.Security;

public sealed class CsrfMiddleware
{
    public const string ClaimType = "tracebag_csrf";
    public const string HeaderName = "X-CSRF-TOKEN";

    private readonly RequestDelegate _next;
    private readonly TracebagOptions _options;

    public CsrfMiddleware(RequestDelegate next, TracebagOptions options)
    {
        _next = next;
        _options = options;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_options.AuthEnabled || !RequiresCsrf(context.Request))
        {
            await _next(context);
            return;
        }

        var expected = context.User.FindFirst(ClaimType)?.Value;
        var actual = context.Request.Headers[HeaderName].ToString();
        if (string.IsNullOrWhiteSpace(expected) || !TimeConstantEquals(expected, actual))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "csrf_token_invalid",
                message = "A valid CSRF token is required."
            }, context.RequestAborted);
            return;
        }

        await _next(context);
    }

    private static bool RequiresCsrf(HttpRequest request)
    {
        if (!request.Path.StartsWithSegments("/api") || request.Path.StartsWithSegments("/api/auth/login"))
        {
            return false;
        }

        return HttpMethods.IsPost(request.Method)
            || HttpMethods.IsPut(request.Method)
            || HttpMethods.IsPatch(request.Method)
            || HttpMethods.IsDelete(request.Method);
    }

    private static bool TimeConstantEquals(string left, string right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        var diff = 0;
        for (var i = 0; i < left.Length; i++)
        {
            diff |= left[i] ^ right[i];
        }

        return diff == 0;
    }
}
