namespace Tracebag.Api.Models;

public sealed record ContainerOverviewDto(
    ContainerDto Container,
    ContainerInspectDto Inspect,
    ContainerResourceStatsDto Resources,
    IReadOnlyList<DockerEventDto> RecentEvents,
    int KnownInstanceCount);

public sealed record ContainerInspectDto(
    string Platform,
    string Driver,
    bool Running,
    bool Paused,
    bool Restarting,
    bool Dead,
    bool OomKilled,
    long Pid,
    long ExitCode,
    long RestartCount,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    ContainerHealthDto Health);

public sealed record ContainerHealthDto(
    string Status,
    long FailingStreak,
    IReadOnlyList<ContainerHealthLogDto> RecentChecks);

public sealed record ContainerHealthLogDto(
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    long ExitCode,
    string Output);

public sealed record ContainerResourceStatsDto(
    bool Available,
    string? UnavailableReason,
    DateTimeOffset? ReadAt,
    double? CpuPercent,
    ulong? MemoryUsageBytes,
    ulong? MemoryLimitBytes,
    double? MemoryPercent,
    ulong? NetworkRxBytes,
    ulong? NetworkTxBytes,
    ulong? BlockReadBytes,
    ulong? BlockWriteBytes,
    ulong? Pids);

public sealed record DockerEventDto(
    long? Id,
    string ContainerId,
    string DockerId,
    string Action,
    DateTimeOffset Timestamp,
    IReadOnlyDictionary<string, string> Attributes);

public sealed record SystemStatusDto(
    string Version,
    DateTimeOffset StartedAt,
    TimeSpan Uptime,
    SystemDependencyDto Docker,
    SystemDependencyDto DiscoveryScope,
    SystemDependencyDto Database,
    SystemDependencyDto ArtifactStorage,
    SystemDependencyDto RunnerImage,
    SystemDependencyDto DataRetention,
    EventCollectorStatusDto EventCollector,
    LogIngestionStatusDto LogIngestion,
    int ActiveTargetCount,
    int KnownTargetCount);

public sealed record SystemDependencyDto(
    string Status,
    string Message,
    IReadOnlyDictionary<string, object?> Details);

public sealed record EventCollectorStatusDto(
    string Status,
    DateTimeOffset? LastConnectedAt,
    DateTimeOffset? LastEventAt,
    int RetainedEventCount,
    string? LastError);
