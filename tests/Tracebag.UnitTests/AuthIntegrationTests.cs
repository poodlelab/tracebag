using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tracebag.UnitTests;

public sealed class AuthIntegrationTests
{
    private const string AdminUser = "admin";
    private const string AdminPassword = "correct horse battery staple";

    [Fact]
    public async Task LoginSetsSecureCookieAndCsrfProtectsLogout()
    {
        using var factory = new TracebagApplicationFactory(AdminUser, AdminPassword);
        using var client = factory.CreateSecureClient();

        var login = await client.PostAsJsonAsync("/api/auth/login", new
        {
            userName = AdminUser,
            password = AdminPassword
        });

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var cookie = Assert.Single(login.Headers.GetValues("Set-Cookie"));
        Assert.Contains("__Host-Tracebag=", cookie, StringComparison.Ordinal);
        Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        var payload = await login.Content.ReadFromJsonAsync<JsonElement>();
        var csrfToken = payload.GetProperty("csrfToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(csrfToken));

        var missingCsrf = await client.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.BadRequest, missingCsrf.StatusCode);
        Assert.Equal("csrf_token_invalid", (await missingCsrf.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        request.Headers.Add("X-CSRF-TOKEN", csrfToken);
        var logout = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);
    }

    [Fact]
    public async Task UnauthenticatedApiIsRejectedWithSecurityHeaders()
    {
        using var factory = new TracebagApplicationFactory(AdminUser, AdminPassword);
        using var client = factory.CreateSecureClient();

        var response = await client.GetAsync("/api/system/status");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var privilegedMutation = await client.PostAsync("/api/containers/not-a-container/restart", null);
        Assert.Equal(HttpStatusCode.Unauthorized, privilegedMutation.StatusCode);
        Assert.Equal("DENY", Header(response, "X-Frame-Options"));
        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
        Assert.Equal("no-referrer", Header(response, "Referrer-Policy"));
        Assert.Contains("frame-ancestors 'none'", Header(response, "Content-Security-Policy"), StringComparison.Ordinal);
        Assert.Equal("max-age=31536000; includeSubDomains", Header(response, "Strict-Transport-Security"));
    }

    [Fact]
    public async Task UnknownUserAndWrongPasswordHaveSamePublicFailure()
    {
        using var factory = new TracebagApplicationFactory(AdminUser, AdminPassword, loginPermitLimit: 5);
        using var client = factory.CreateSecureClient();

        var unknownUser = await client.PostAsJsonAsync("/api/auth/login", new
        {
            userName = "unknown",
            password = AdminPassword
        });
        var wrongPassword = await client.PostAsJsonAsync("/api/auth/login", new
        {
            userName = AdminUser,
            password = "wrong"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, unknownUser.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(await unknownUser.Content.ReadAsStringAsync(), await wrongPassword.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task LoginRateLimitRejectsWithoutQueueing()
    {
        using var factory = new TracebagApplicationFactory(AdminUser, AdminPassword, loginPermitLimit: 2);
        using var client = factory.CreateSecureClient();

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var rejectedCredentials = await client.PostAsJsonAsync("/api/auth/login", new
            {
                userName = AdminUser,
                password = "wrong"
            });
            Assert.Equal(HttpStatusCode.Unauthorized, rejectedCredentials.StatusCode);
        }

        var rateLimited = await client.PostAsJsonAsync("/api/auth/login", new
        {
            userName = AdminUser,
            password = AdminPassword
        });

        Assert.Equal(HttpStatusCode.TooManyRequests, rateLimited.StatusCode);
        Assert.True(rateLimited.Headers.Contains("Retry-After"));
        Assert.Equal("login_rate_limited", (await rateLimited.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
    }

    [Fact]
    public async Task OversizedCredentialsUseBoundedInvalidLoginResponse()
    {
        using var factory = new TracebagApplicationFactory(AdminUser, AdminPassword);
        using var client = factory.CreateSecureClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            userName = new string('u', 161),
            password = new string('p', 1_025)
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("invalid_login", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
    }

    [Fact]
    public async Task TrustedForwardedHeadersRestoreHttpsScheme()
    {
        using var factory = new TracebagApplicationFactory(AdminUser, AdminPassword);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("http://localhost"),
            HandleCookies = false
        });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add("X-Forwarded-For", "198.51.100.10");
        request.Headers.Add("X-Forwarded-Proto", "https");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("max-age=31536000; includeSubDomains", Header(response, "Strict-Transport-Security"));
    }

    private static string Header(HttpResponseMessage response, string name)
    {
        return Assert.Single(response.Headers.GetValues(name));
    }

    private sealed class TracebagApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"tracebag-auth-tests-{Guid.NewGuid():N}");
        private readonly Dictionary<string, string?> _configuration;

        public TracebagApplicationFactory(string adminUser, string adminPassword, int loginPermitLimit = 5)
        {
            var passwordHash = new PasswordHasher<string>().HashPassword(adminUser, adminPassword);
            _configuration = new Dictionary<string, string?>
            {
                ["TRACEBAG_STAGE"] = "local",
                ["TRACEBAG_AUTH_ENABLED"] = "true",
                ["TRACEBAG_ADMIN_USER"] = adminUser,
                ["TRACEBAG_ADMIN_PASSWORD_HASH"] = passwordHash,
                ["TRACEBAG_AUTH_LOGIN_PERMIT_LIMIT"] = loginPermitLimit.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["TRACEBAG_AUTH_LOGIN_WINDOW_SECONDS"] = "600",
                ["TRACEBAG_TRUSTED_PROXIES"] = "127.0.0.1,::1",
                ["TRACEBAG_DATA_DIR"] = Path.Combine(_root, "data"),
                ["TRACEBAG_ARTIFACT_DIR"] = Path.Combine(_root, "artifacts")
            };
        }

        public HttpClient CreateSecureClient()
        {
            return CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://localhost"),
                HandleCookies = true
            });
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            foreach (var (key, value) in _configuration)
            {
                builder.UseSetting(key, value);
            }

            builder.ConfigureTestServices(services =>
            {
                var hostedServices = services.Where(descriptor => descriptor.ServiceType == typeof(IHostedService)).ToArray();
                foreach (var hostedService in hostedServices)
                {
                    services.Remove(hostedService);
                }
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }
}
