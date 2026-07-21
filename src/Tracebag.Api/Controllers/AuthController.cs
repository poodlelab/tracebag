using System.Security.Claims;
using Tracebag.Api.Audit;
using Tracebag.Api.Auth;
using Tracebag.Api.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Tracebag.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    internal const int MaximumUserNameLength = 160;
    internal const int MaximumPasswordLength = 1_024;
    internal const int MaximumLoginBodyBytes = 4_096;
    private readonly TracebagOptions _options;
    private readonly AuditLog _auditLog;

    public AuthController(TracebagOptions options, AuditLog auditLog)
    {
        _options = options;
        _auditLog = auditLog;
    }

    [HttpPost("login")]
    [EnableRateLimiting("login")]
    [RequestSizeLimit(MaximumLoginBodyBytes)]
    public async Task<IActionResult> Login([FromBody] LoginRequest? request, CancellationToken cancellationToken)
    {
        request ??= new LoginRequest();
        if (!_options.AuthEnabled)
        {
            return Ok(new { authenticated = true, user = "auth-disabled", csrfToken = string.Empty });
        }

        var suppliedUserName = request.UserName ?? string.Empty;
        var suppliedPassword = request.Password ?? string.Empty;
        var inputWithinBounds = suppliedUserName.Length <= MaximumUserNameLength
            && suppliedPassword.Length <= MaximumPasswordLength;
        var hasher = new PasswordHasher<string>();
        var result = hasher.VerifyHashedPassword(
            _options.AdminUser,
            _options.AdminPasswordHash,
            inputWithinBounds ? suppliedPassword : string.Empty);
        var valid = inputWithinBounds
            && string.Equals(suppliedUserName, _options.AdminUser, StringComparison.Ordinal)
            && result != PasswordVerificationResult.Failed;
        if (!valid)
        {
            await _auditLog.WriteAsync(suppliedUserName, "auth.login", null, null, "failed", null, cancellationToken);
            return Unauthorized(new { error = "invalid_login", message = "Invalid login." });
        }

        var csrfToken = TokenGenerator.Create();
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, _options.AdminUser),
            new(CsrfMiddleware.ClaimType, csrfToken)
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
        await _auditLog.WriteAsync(_options.AdminUser, "auth.login", null, null, "success", null, cancellationToken);
        return Ok(new { authenticated = true, user = _options.AdminUser, csrfToken });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var user = User.Identity?.Name ?? "anonymous";
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        await _auditLog.WriteAsync(user, "auth.logout", null, null, "success", null, cancellationToken);
        return Ok(new { authenticated = false });
    }

    [HttpGet("me")]
    public IActionResult Me()
    {
        if (!_options.AuthEnabled)
        {
            return Ok(new { authenticated = true, user = "auth-disabled" });
        }

        return Ok(new
        {
            authenticated = User.Identity?.IsAuthenticated == true,
            user = User.Identity?.IsAuthenticated == true ? User.Identity.Name : null
        });
    }

    [HttpGet("csrf")]
    public IActionResult Csrf()
    {
        var token = User.FindFirst(CsrfMiddleware.ClaimType)?.Value;
        if (_options.AuthEnabled && string.IsNullOrWhiteSpace(token))
        {
            return Unauthorized(new { error = "not_authenticated", message = "Login required." });
        }

        return Ok(new { csrfToken = token ?? string.Empty });
    }
}

public sealed class LoginRequest
{
    public string? UserName { get; init; }
    public string? Password { get; init; }
}
