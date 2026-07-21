using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using System.Threading.RateLimiting;
using Tracebag.Api.Artifacts;
using Tracebag.Api.Analysis;
using Tracebag.Api.Audit;
using Tracebag.Api.Auth;
using Tracebag.Api.Data;
using Tracebag.Api.Diagnostics;
using Tracebag.Api.Docker;
using Tracebag.Api.Health;
using Tracebag.Api.Incidents;
using Tracebag.Api.Logs;
using Tracebag.Api.Retention;
using Tracebag.Api.Security;

if (args.FirstOrDefault() == "hash-password")
{
    var user = args.Skip(1).FirstOrDefault() ?? "admin";
    var password = args.Skip(2).FirstOrDefault();
    if (string.IsNullOrWhiteSpace(password) && Console.IsInputRedirected)
    {
        password = Console.In.ReadLine();
    }

    if (string.IsNullOrWhiteSpace(password))
    {
        Console.Error.WriteLine("Usage: dotnet Tracebag.Api.dll hash-password <user> [password]");
        return;
    }

    Console.WriteLine(new PasswordHasher<string>().HashPassword(user, password));
    return;
}

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

var localUiDist = Path.GetFullPath(Path.Combine(
    builder.Environment.ContentRootPath,
    "..",
    "Tracebag.Web",
    "dist",
    "tracebag-web",
    "browser"));

var tracebagOptions = TracebagOptions.FromConfiguration(builder.Configuration);
tracebagOptions.ValidateForStartup();

Directory.CreateDirectory(tracebagOptions.ArtifactDir);
Directory.CreateDirectory(tracebagOptions.DataDir);
Directory.CreateDirectory(Path.Combine(tracebagOptions.DataDir, "data-protection-keys"));

builder.Services.AddSingleton(tracebagOptions);
builder.Services.AddDataProtection()
    .SetApplicationName($"Tracebag-{tracebagOptions.Stage}")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(tracebagOptions.DataDir, "data-protection-keys")));

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "__Host-Tracebag";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds)
                .ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            error = "login_rate_limited",
            message = "Too many login attempts. Try again later."
        }, cancellationToken);
    };
    options.AddPolicy("login", context =>
    {
        var client = context.Connection.RemoteIpAddress?.MapToIPv6().ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(client, _ => new FixedWindowRateLimiterOptions
        {
            AutoReplenishment = true,
            PermitLimit = tracebagOptions.AuthLoginPermitLimit,
            QueueLimit = 0,
            Window = TimeSpan.FromSeconds(tracebagOptions.AuthLoginWindowSeconds)
        });
    });
});
builder.Services.AddControllers();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"])
    .AddCheck<DockerHealthCheck>("docker", tags: ["ready"])
    .AddCheck<ArtifactStorageHealthCheck>("artifact-storage", tags: ["ready"]);

if (tracebagOptions.DatabaseEnabled)
{
    builder.Services.AddDbContextFactory<TracebagDbContext>(options =>
        options.UseNpgsql(tracebagOptions.DatabaseUrl));
    builder.Services.AddHostedService<TracebagDatabaseMigrator>();
}

builder.Services.AddSingleton<DockerClientFactory>();
builder.Services.AddSingleton<ContainerIdentityResolver>();
builder.Services.AddSingleton<ContainerPolicy>();
builder.Services.AddSingleton(sp => tracebagOptions.DatabaseEnabled
    ? new ContainerTargetRegistry(
        sp.GetRequiredService<ContainerPolicy>(),
        sp.GetRequiredService<IDbContextFactory<TracebagDbContext>>())
    : new ContainerTargetRegistry(sp.GetRequiredService<ContainerPolicy>()));
builder.Services.AddSingleton<ContainerCatalog>();
builder.Services.AddSingleton(sp => tracebagOptions.DatabaseEnabled
    ? new DockerEventCollector(
        sp.GetRequiredService<DockerClientFactory>(),
        sp.GetRequiredService<ContainerPolicy>(),
        sp.GetRequiredService<ContainerIdentityResolver>(),
        sp.GetRequiredService<ILogger<DockerEventCollector>>(),
        sp.GetRequiredService<IDbContextFactory<TracebagDbContext>>())
    : new DockerEventCollector(
        sp.GetRequiredService<DockerClientFactory>(),
        sp.GetRequiredService<ContainerPolicy>(),
        sp.GetRequiredService<ContainerIdentityResolver>(),
        sp.GetRequiredService<ILogger<DockerEventCollector>>()));
builder.Services.AddHostedService(sp => sp.GetRequiredService<DockerEventCollector>());
builder.Services.AddSingleton(sp => tracebagOptions.DatabaseEnabled
    ? new ContainerOperationalService(
        sp.GetRequiredService<DockerClientFactory>(),
        sp.GetRequiredService<ContainerCatalog>(),
        sp.GetRequiredService<ContainerPolicy>(),
        sp.GetRequiredService<DockerEventCollector>(),
        sp.GetRequiredService<IDbContextFactory<TracebagDbContext>>())
    : new ContainerOperationalService(
        sp.GetRequiredService<DockerClientFactory>(),
        sp.GetRequiredService<ContainerCatalog>(),
        sp.GetRequiredService<ContainerPolicy>(),
        sp.GetRequiredService<DockerEventCollector>()));
builder.Services.AddSingleton<DockerLogService>();
builder.Services.AddSingleton<LogParserChain>();
builder.Services.AddSingleton<LogTargetPolicy>();
builder.Services.AddSingleton<LogLiveHub>();
builder.Services.AddSingleton(sp => tracebagOptions.DatabaseEnabled
    ? new LogStore(tracebagOptions, sp.GetRequiredService<IDbContextFactory<TracebagDbContext>>())
    : new LogStore(tracebagOptions));
builder.Services.AddSingleton<LogIngestionCoordinator>();
if (tracebagOptions.DatabaseEnabled && tracebagOptions.LogIngestionEnabled)
{
    builder.Services.AddHostedService(sp => sp.GetRequiredService<LogIngestionCoordinator>());
    builder.Services.AddHostedService<LogRetentionService>();
}
if (tracebagOptions.DatabaseEnabled)
{
    builder.Services.AddSingleton<DurableRetentionStore>();
    builder.Services.AddHostedService<DurableRetentionService>();
}
builder.Services.AddSingleton(sp => tracebagOptions.DatabaseEnabled
    ? new SystemStatusService(
        sp.GetRequiredService<DockerClientFactory>(),
        sp.GetRequiredService<ContainerCatalog>(),
        sp.GetRequiredService<ContainerTargetRegistry>(),
        sp.GetRequiredService<DockerEventCollector>(),
        tracebagOptions,
        sp.GetRequiredService<IDbContextFactory<TracebagDbContext>>(),
        sp.GetRequiredService<LogIngestionCoordinator>(),
        sp.GetRequiredService<DurableRetentionStore>())
    : new SystemStatusService(
        sp.GetRequiredService<DockerClientFactory>(),
        sp.GetRequiredService<ContainerCatalog>(),
        sp.GetRequiredService<ContainerTargetRegistry>(),
        sp.GetRequiredService<DockerEventCollector>(),
        tracebagOptions));
builder.Services.AddSingleton<CounterPresetCatalog>();
builder.Services.AddSingleton<DiagnosticRunnerCatalog>();
builder.Services.AddSingleton<DiagnosticRunnerContainerPolicy>();
builder.Services.AddSingleton<DiagnosticJobProfileCatalog>();
builder.Services.AddSingleton<DiagnosticSessionRegistry>();
builder.Services.AddSingleton<DiagnosticRunnerService>();
builder.Services.AddSingleton<CounterSampleParser>();
builder.Services.AddSingleton(sp => tracebagOptions.DatabaseEnabled
    ? new CounterRecordingStore(tracebagOptions, sp.GetRequiredService<IDbContextFactory<TracebagDbContext>>())
    : new CounterRecordingStore(tracebagOptions));
builder.Services.AddSingleton<CounterRecordingService>();
builder.Services.AddSingleton(sp => tracebagOptions.DatabaseEnabled
    ? new ArtifactStore(tracebagOptions, sp.GetRequiredService<IDbContextFactory<TracebagDbContext>>())
    : new ArtifactStore(tracebagOptions));
builder.Services.AddSingleton(sp => tracebagOptions.DatabaseEnabled
    ? new AuditLog(tracebagOptions, sp.GetRequiredService<IDbContextFactory<TracebagDbContext>>())
    : new AuditLog(tracebagOptions));
if (tracebagOptions.DatabaseEnabled)
{
    builder.Services.AddHostedService<AuditRetentionService>();
}
builder.Services.AddHostedService<ArtifactRetentionService>();
if (tracebagOptions.DatabaseEnabled)
{
    builder.Services.AddSingleton<DiagnosticJobStore>();
    builder.Services.AddSingleton<DiagnosticJobService>();
    builder.Services.AddHostedService<DiagnosticJobRecoveryService>();
    builder.Services.AddHostedService<CounterRecordingRecoveryService>();
    builder.Services.AddHostedService<CounterRecordingRetentionService>();
    builder.Services.AddSingleton<GuidedIncidentProfileCatalog>();
    builder.Services.AddSingleton<StackSnapshotAnalyzer>();
    builder.Services.AddSingleton<NetTraceAnalyzer>();
    builder.Services.AddSingleton<LocalAnalysisService>();
    builder.Services.AddSingleton<IncidentService>();
    builder.Services.AddSingleton<TracebagExportService>();
    builder.Services.AddHostedService<IncidentRecoveryService>();
    builder.Services.AddHostedService<LocalAnalysisRecoveryService>();
}

var app = builder.Build();

app.UseForwardedHeaders(ForwardedHeadersPolicy.Create(tracebagOptions));
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseMiddleware<ErrorHandlingMiddleware>();
UseTracebagStaticFiles(app, localUiDist);
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<TracebagAccessMiddleware>();
app.UseMiddleware<CsrfMiddleware>();
app.UseAuthorization();
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = HealthResponseWriter.WriteAsync
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
    ResponseWriter = HealthResponseWriter.WriteAsync
});
app.MapControllers();
MapTracebagFallback(app, localUiDist);

app.Run();

static void UseTracebagStaticFiles(WebApplication app, string localUiDist)
{
    var staticRoot = ResolveStaticRoot(app, localUiDist);
    if (staticRoot is null)
    {
        return;
    }

    var fileProvider = new PhysicalFileProvider(staticRoot);
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = fileProvider });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = fileProvider });
}

static void MapTracebagFallback(WebApplication app, string localUiDist)
{
    var staticRoot = ResolveStaticRoot(app, localUiDist);
    if (staticRoot is null)
    {
        return;
    }

    var indexPath = Path.Combine(staticRoot, "index.html");
    if (!File.Exists(indexPath))
    {
        return;
    }

    app.MapFallback(async context =>
    {
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.SendFileAsync(indexPath, context.RequestAborted);
    });
}

static string? ResolveStaticRoot(WebApplication app, string localUiDist)
{
    var containerWebRoot = Path.Combine(app.Environment.ContentRootPath, "wwwroot");
    if (Directory.Exists(containerWebRoot))
    {
        return containerWebRoot;
    }

    return Directory.Exists(localUiDist) ? localUiDist : null;
}

public partial class Program
{
}
