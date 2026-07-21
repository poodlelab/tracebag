using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Tracebag.Api.Auth;
using Tracebag.Api.Data;
using Tracebag.Api.Models;
using Tracebag.Api.Logs;
using Tracebag.Api.Retention;

namespace Tracebag.Api.Docker;

public sealed class SystemStatusService
{
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;
    private readonly DockerClientFactory _dockerClientFactory;
    private readonly ContainerCatalog _containerCatalog;
    private readonly ContainerTargetRegistry _targetRegistry;
    private readonly DockerEventCollector _eventCollector;
    private readonly TracebagOptions _options;
    private readonly IDbContextFactory<TracebagDbContext>? _dbContextFactory;
    private readonly LogIngestionCoordinator? _logIngestionCoordinator;
    private readonly DurableRetentionStore? _durableRetention;

    public SystemStatusService(
        DockerClientFactory dockerClientFactory,
        ContainerCatalog containerCatalog,
        ContainerTargetRegistry targetRegistry,
        DockerEventCollector eventCollector,
        TracebagOptions options,
        IDbContextFactory<TracebagDbContext>? dbContextFactory = null,
        LogIngestionCoordinator? logIngestionCoordinator = null,
        DurableRetentionStore? durableRetention = null)
    {
        _dockerClientFactory = dockerClientFactory;
        _containerCatalog = containerCatalog;
        _targetRegistry = targetRegistry;
        _eventCollector = eventCollector;
        _options = options;
        _dbContextFactory = dbContextFactory;
        _logIngestionCoordinator = logIngestionCoordinator;
        _durableRetention = durableRetention;
    }

    public async Task<SystemStatusDto> GetAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ContainerDto> activeTargets = [];
        SystemDependencyDto docker;
        try
        {
            var info = await _dockerClientFactory.Client.System.GetSystemInfoAsync(cancellationToken);
            activeTargets = await _containerCatalog.ListAllowedAsync(cancellationToken);
            docker = Healthy("Docker Engine is reachable.", new Dictionary<string, object?>
            {
                ["serverVersion"] = info.ServerVersion,
                ["operatingSystem"] = info.OperatingSystem,
                ["architecture"] = info.Architecture,
                ["containersRunning"] = info.ContainersRunning,
                ["cpus"] = info.NCPU,
                ["memoryBytes"] = info.MemTotal
            });
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            docker = Unavailable("Docker Engine is unreachable.");
        }

        var database = await DatabaseStatusAsync(cancellationToken);
        var artifacts = ArtifactStatus();
        var runner = await RunnerStatusAsync(cancellationToken);
        var dataRetention = await DataRetentionStatusAsync(cancellationToken);
        var logIngestion = _logIngestionCoordinator is null
            ? new LogIngestionStatusDto(
                "disabled", 0, 0, 0, 0, 0, 0, 0, 0, 0, null, null, null,
                "Persistent log ingestion requires PostgreSQL.")
            : await _logIngestionCoordinator.StatusAsync(cancellationToken);
        var targets = _targetRegistry.Snapshot();

        return new SystemStatusDto(
            Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
                ?? "unknown",
            _startedAt,
            DateTimeOffset.UtcNow - _startedAt,
            docker,
            CreateDiscoveryScopeStatus(_options),
            database,
            artifacts,
            runner,
            dataRetention,
            _eventCollector.Status(),
            logIngestion,
            activeTargets.Count,
            targets.Count);
    }

    internal static SystemDependencyDto CreateDiscoveryScopeStatus(TracebagOptions options)
    {
        var details = new Dictionary<string, object?>
        {
            ["allowedLabel"] = $"{options.AllowedLabelKey}={options.AllowedLabelValue}",
            ["environmentLabel"] = options.EnvironmentLabelKey is null
                ? null
                : $"{options.EnvironmentLabelKey}={options.EnvironmentLabelValue}"
        };

        return options.EnvironmentLabelKey is null
            ? new SystemDependencyDto(
                "attention",
                "No environment scope is configured. Every container with the allowed label on this Docker host is visible to this Tracebag instance.",
                details)
            : Healthy("Container discovery is restricted by both opt-in and environment labels.", details);
    }

    private async Task<SystemDependencyDto> DataRetentionStatusAsync(CancellationToken cancellationToken)
    {
        if (_durableRetention is null)
        {
            return new SystemDependencyDto(
                "development-fallback",
                "Durable retention requires PostgreSQL.",
                new Dictionary<string, object?>());
        }

        try
        {
            var snapshot = await _durableRetention.StatusAsync(cancellationToken);
            var status = snapshot.LastError is not null
                ? "degraded"
                : snapshot.Incidents >= snapshot.IncidentMaxCount
                    ? "attention"
                    : "healthy";
            var message = status switch
            {
                "degraded" => "The latest durable retention pass failed; data was preserved for retry.",
                "attention" => "Incident capacity is full; export and delete an incident before creating another.",
                _ => "Durable retention limits are active."
            };
            return new SystemDependencyDto(status, message, new Dictionary<string, object?>
            {
                ["diagnosticJobs"] = snapshot.DiagnosticJobs,
                ["expiredJobsEligible"] = snapshot.ExpiredJobsEligible,
                ["expiredJobsProtectedByIncidents"] = snapshot.ExpiredJobsProtectedByIncidents,
                ["incidents"] = snapshot.Incidents,
                ["incidentMaxCount"] = snapshot.IncidentMaxCount,
                ["activeIncidents"] = snapshot.ActiveIncidents,
                ["incidentArtifactReferences"] = snapshot.IncidentArtifactReferences,
                ["incidentRecordingReferences"] = snapshot.IncidentRecordingReferences,
                ["auditEvents"] = snapshot.AuditEvents,
                ["auditMaxEvents"] = snapshot.AuditMaxEvents,
                ["lastCompletedAt"] = snapshot.LastCompletedAt,
                ["lastDeletedJobs"] = snapshot.LastDeletedJobs,
                ["lastError"] = snapshot.LastError
            });
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return Unavailable("Durable retention status is unavailable.");
        }
    }

    private async Task<SystemDependencyDto> DatabaseStatusAsync(CancellationToken cancellationToken)
    {
        if (_dbContextFactory is null)
        {
            return new SystemDependencyDto(
                "development-fallback",
                "PostgreSQL is not configured.",
                new Dictionary<string, object?>());
        }

        try
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            var connected = await db.Database.CanConnectAsync(cancellationToken);
            return connected
                ? Healthy("PostgreSQL is reachable.", new Dictionary<string, object?>())
                : Unavailable("PostgreSQL is unreachable.");
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return Unavailable("PostgreSQL is unreachable.");
        }
    }

    private SystemDependencyDto ArtifactStatus()
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(_options.ArtifactDir)) ?? _options.ArtifactDir;
            var drive = new DriveInfo(root);
            return Healthy("Artifact storage is available.", new Dictionary<string, object?>
            {
                ["freeBytes"] = drive.AvailableFreeSpace,
                ["totalBytes"] = drive.TotalSize
            });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Unavailable("Artifact storage information is unavailable.");
        }
    }

    private async Task<SystemDependencyDto> RunnerStatusAsync(CancellationToken cancellationToken)
    {
        var configured = new Dictionary<int, string>
        {
            [8] = _options.DiagnosticImage,
            [9] = _options.DiagnosticImageDotnet9,
            [10] = _options.DiagnosticImageDotnet10
        };
        var images = new Dictionary<string, object?>();
        var defaultAvailable = false;
        foreach (var (runtime, imageName) in configured)
        {
            try
            {
                var image = await _dockerClientFactory.Client.Images.InspectImageAsync(imageName, cancellationToken);
                images[runtime.ToString(System.Globalization.CultureInfo.InvariantCulture)] = new
                {
                    image = imageName,
                    imageId = ShortId(image.ID),
                    available = true
                };
                defaultAvailable |= runtime == _options.DiagnosticDefaultRuntimeMajor;
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                images[runtime.ToString(System.Globalization.CultureInfo.InvariantCulture)] = new
                {
                    image = imageName,
                    available = false
                };
            }
        }

        var details = new Dictionary<string, object?>
        {
            ["defaultRuntimeMajor"] = _options.DiagnosticDefaultRuntimeMajor,
            ["runtimes"] = images
        };
        return defaultAvailable
            ? Healthy("The default .NET runner image is available.", details)
            : new SystemDependencyDto(
                "on-demand",
                "The default .NET runner will be downloaded from its configured registry when diagnostics are first used.",
                details);
    }

    private static SystemDependencyDto Healthy(string message, IReadOnlyDictionary<string, object?> details)
    {
        return new SystemDependencyDto("healthy", message, details);
    }

    private static SystemDependencyDto Unavailable(
        string message,
        IReadOnlyDictionary<string, object?>? details = null)
    {
        return new SystemDependencyDto(
            "unavailable",
            message,
            details ?? new Dictionary<string, object?>());
    }

    private static string ShortId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        return value.Length <= 20 ? value : value[..20];
    }
}
