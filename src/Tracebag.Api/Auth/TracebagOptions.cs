namespace Tracebag.Api.Auth;

public sealed record TracebagOptions
{
    public required string Stage { get; init; }
    public required bool AuthEnabled { get; init; }
    public required string AdminUser { get; init; }
    public required string AdminPasswordHash { get; init; }
    public int AuthLoginPermitLimit { get; init; } = 5;
    public int AuthLoginWindowSeconds { get; init; } = 60;
    public IReadOnlyList<string> TrustedProxies { get; init; } = [];
    public int AuditRetentionDays { get; init; } = 30;
    public int AuditMaxEvents { get; init; } = 100_000;
    public int AuditRetentionDeleteBatchSize { get; init; } = 1_000;
    public int AuditRetentionScanSeconds { get; init; } = 300;
    public int DiagnosticJobRetentionDays { get; init; } = 30;
    public int DiagnosticJobRetentionDeleteBatchSize { get; init; } = 100;
    public int DurableRetentionScanSeconds { get; init; } = 300;
    public int IncidentMaxCount { get; init; } = 200;
    public required string AllowedLabelKey { get; init; }
    public required string AllowedLabelValue { get; init; }
    public string? EnvironmentLabelKey { get; init; }
    public string? EnvironmentLabelValue { get; init; }
    public required string ArtifactDir { get; init; }
    public required string DataDir { get; init; }
    public required string ArtifactVolume { get; init; }
    public required string DiagnosticImage { get; init; }
    public string DiagnosticImageDotnet9 { get; init; } = "tracebag-runner-dotnet-9:dev";
    public string DiagnosticImageDotnet10 { get; init; } = "tracebag-runner-dotnet-10:dev";
    public int DiagnosticDefaultRuntimeMajor { get; init; } = 8;
    public long RunnerMemoryLimitBytes { get; init; } = 1_073_741_824;
    public int RunnerCpuLimitMillicores { get; init; } = 1_000;
    public int RunnerPidsLimit { get; init; } = 128;
    public required string PublicBaseUrl { get; init; }
    public required bool RestartEnabled { get; init; }
    public required int CounterMaxSeconds { get; init; }
    public required int ArtifactRetentionHours { get; init; }
    public required int ArtifactMaxCount { get; init; }
    public required long ArtifactMaxTotalBytes { get; init; }
    public string? DatabaseUrl { get; init; }
    public bool CounterRecordingEnabled { get; init; } = true;
    public int CounterRecordingDefaultIntervalSeconds { get; init; } = 5;
    public int CounterRecordingMaxDurationMinutes { get; init; } = 1440;
    public int CounterRecordingMaxActiveGlobal { get; init; } = 3;
    public int CounterRecordingRetentionDays { get; init; } = 7;
    public bool LogIngestionEnabled { get; init; } = true;
    public int LogChannelCapacity { get; init; } = 5_000;
    public int LogBatchSize { get; init; } = 200;
    public int LogFlushMilliseconds { get; init; } = 1_000;
    public int LogCollectorScanSeconds { get; init; } = 5;
    public int LogRetentionDays { get; init; } = 7;
    public long LogMaxTotalBytes { get; init; } = 1_073_741_824;
    public long LogMaxBytesPerContainer { get; init; } = 268_435_456;
    public int LogRetentionDeleteBatchSize { get; init; } = 1_000;
    public int LogRetentionScanSeconds { get; init; } = 60;
    public int LogMaxLineBytes { get; init; } = 262_144;
    public int DiagnosticJobMaxActiveGlobal { get; init; } = 2;
    public int DiagnosticJobDailyLimit { get; init; } = 25;
    public int DiagnosticJobMaxDurationSeconds { get; init; } = 600;
    public bool FullDumpEnabled { get; init; }
    public long AnalysisMaxTraceBytes { get; init; } = 536_870_912;
    public long AnalysisMaxStackBytes { get; init; } = 8_388_608;
    public int AnalysisMaxEvents { get; init; } = 2_000_000;

    public bool DatabaseEnabled => !string.IsNullOrWhiteSpace(DatabaseUrl);

    public void ValidateForStartup()
    {
        if (AuthEnabled && string.IsNullOrWhiteSpace(AdminPasswordHash))
        {
            throw new InvalidOperationException("TRACEBAG_ADMIN_PASSWORD_HASH must be set when TRACEBAG_AUTH_ENABLED=true.");
        }

        if (!AuthEnabled && !string.Equals(Stage, "local", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Authentication may only be disabled when TRACEBAG_STAGE=local.");
        }
    }

    public static TracebagOptions FromConfiguration(IConfiguration configuration)
    {
        var stage = configuration["TRACEBAG_STAGE"] ?? "local";
        var allowed = SplitLabel(configuration["TRACEBAG_ALLOWED_LABEL"] ?? "tracebag.enabled=true");
        var environmentLabel = SplitOptionalLabel(configuration["TRACEBAG_ENVIRONMENT_LABEL"]);

        return new TracebagOptions
        {
            Stage = stage,
            AuthEnabled = ParseBool(configuration["TRACEBAG_AUTH_ENABLED"], defaultValue: true),
            AdminUser = configuration["TRACEBAG_ADMIN_USER"] ?? "admin",
            AdminPasswordHash = configuration["TRACEBAG_ADMIN_PASSWORD_HASH"] ?? string.Empty,
            AuthLoginPermitLimit = ParseInt(configuration["TRACEBAG_AUTH_LOGIN_PERMIT_LIMIT"], 5, min: 2, max: 100),
            AuthLoginWindowSeconds = ParseInt(configuration["TRACEBAG_AUTH_LOGIN_WINDOW_SECONDS"], 60, min: 10, max: 3_600),
            TrustedProxies = ParseList(configuration["TRACEBAG_TRUSTED_PROXIES"]),
            AuditRetentionDays = ParseInt(configuration["TRACEBAG_AUDIT_RETENTION_DAYS"], 30, min: 1, max: 3_650),
            AuditMaxEvents = ParseInt(configuration["TRACEBAG_AUDIT_MAX_EVENTS"], 100_000, min: 1_000, max: 10_000_000),
            AuditRetentionDeleteBatchSize = ParseInt(configuration["TRACEBAG_AUDIT_RETENTION_DELETE_BATCH_SIZE"], 1_000, min: 100, max: 10_000),
            AuditRetentionScanSeconds = ParseInt(configuration["TRACEBAG_AUDIT_RETENTION_SCAN_SECONDS"], 300, min: 60, max: 86_400),
            DiagnosticJobRetentionDays = ParseInt(configuration["TRACEBAG_DIAGNOSTIC_JOB_RETENTION_DAYS"], 30, min: 1, max: 3_650),
            DiagnosticJobRetentionDeleteBatchSize = ParseInt(configuration["TRACEBAG_DIAGNOSTIC_JOB_RETENTION_DELETE_BATCH_SIZE"], 100, min: 10, max: 1_000),
            DurableRetentionScanSeconds = ParseInt(configuration["TRACEBAG_DURABLE_RETENTION_SCAN_SECONDS"], 300, min: 60, max: 86_400),
            IncidentMaxCount = ParseInt(configuration["TRACEBAG_INCIDENT_MAX_COUNT"], 200, min: 10, max: 100_000),
            AllowedLabelKey = allowed.Key,
            AllowedLabelValue = allowed.Value,
            EnvironmentLabelKey = environmentLabel?.Key,
            EnvironmentLabelValue = environmentLabel?.Value,
            ArtifactDir = configuration["TRACEBAG_ARTIFACT_DIR"] ?? "/artifacts",
            DataDir = configuration["TRACEBAG_DATA_DIR"] ?? "/data",
            ArtifactVolume = configuration["TRACEBAG_ARTIFACT_VOLUME"] ?? "tracebag_artifacts",
            DiagnosticImage = configuration["TRACEBAG_RUNNER_IMAGE_DOTNET_8"] ?? "tracebag-runner-dotnet-8:dev",
            DiagnosticImageDotnet9 = configuration["TRACEBAG_RUNNER_IMAGE_DOTNET_9"] ?? "tracebag-runner-dotnet-9:dev",
            DiagnosticImageDotnet10 = configuration["TRACEBAG_RUNNER_IMAGE_DOTNET_10"] ?? "tracebag-runner-dotnet-10:dev",
            DiagnosticDefaultRuntimeMajor = ParseInt(configuration["TRACEBAG_RUNNER_DEFAULT_RUNTIME_MAJOR"], 8, min: 8, max: 10),
            RunnerMemoryLimitBytes = ParseLong(configuration["TRACEBAG_RUNNER_MEMORY_LIMIT_BYTES"], 1_073_741_824, min: 134_217_728, max: 8_589_934_592),
            RunnerCpuLimitMillicores = ParseInt(configuration["TRACEBAG_RUNNER_CPU_LIMIT_MILLICORES"], 1_000, min: 100, max: 8_000),
            RunnerPidsLimit = ParseInt(configuration["TRACEBAG_RUNNER_PIDS_LIMIT"], 128, min: 32, max: 512),
            PublicBaseUrl = configuration["TRACEBAG_PUBLIC_URL"] ?? "http://localhost:9090",
            RestartEnabled = ParseBool(configuration["TRACEBAG_RESTART_ENABLED"], defaultValue: false),
            CounterMaxSeconds = ParseInt(configuration["TRACEBAG_COUNTER_MAX_SECONDS"], 600, min: 30, max: 3600),
            ArtifactRetentionHours = ParseInt(configuration["TRACEBAG_ARTIFACT_RETENTION_HOURS"], 24, min: 1, max: 24 * 30),
            ArtifactMaxCount = ParseInt(configuration["TRACEBAG_ARTIFACT_MAX_COUNT"], 20, min: 1, max: 1000),
            ArtifactMaxTotalBytes = ParseLong(configuration["TRACEBAG_ARTIFACT_MAX_TOTAL_BYTES"], 2_147_483_648L, min: 1_048_576L),
            DatabaseUrl = FirstNonEmpty(configuration["TRACEBAG_DATABASE_URL"]),
            CounterRecordingEnabled = ParseBool(configuration["TRACEBAG_COUNTER_RECORDING_ENABLED"], defaultValue: true),
            CounterRecordingDefaultIntervalSeconds = ParseInt(configuration["TRACEBAG_COUNTER_RECORDING_DEFAULT_INTERVAL_SECONDS"], 5, min: 2, max: 10),
            CounterRecordingMaxDurationMinutes = ParseInt(configuration["TRACEBAG_COUNTER_RECORDING_MAX_DURATION_MINUTES"], 1440, min: 1, max: 1440),
            CounterRecordingMaxActiveGlobal = ParseInt(configuration["TRACEBAG_COUNTER_RECORDING_MAX_ACTIVE_GLOBAL"], 3, min: 1, max: 20),
            CounterRecordingRetentionDays = ParseInt(configuration["TRACEBAG_COUNTER_RECORDING_RETENTION_DAYS"], 7, min: 1, max: 90),
            LogIngestionEnabled = ParseBool(configuration["TRACEBAG_LOG_INGESTION_ENABLED"], defaultValue: true),
            LogChannelCapacity = ParseInt(configuration["TRACEBAG_LOG_CHANNEL_CAPACITY"], 5_000, min: 100, max: 100_000),
            LogBatchSize = ParseInt(configuration["TRACEBAG_LOG_BATCH_SIZE"], 200, min: 10, max: 2_000),
            LogFlushMilliseconds = ParseInt(configuration["TRACEBAG_LOG_FLUSH_MILLISECONDS"], 1_000, min: 100, max: 10_000),
            LogCollectorScanSeconds = ParseInt(configuration["TRACEBAG_LOG_COLLECTOR_SCAN_SECONDS"], 5, min: 1, max: 60),
            LogRetentionDays = ParseInt(configuration["TRACEBAG_LOG_RETENTION_DAYS"], 7, min: 1, max: 90),
            LogMaxTotalBytes = ParseLong(configuration["TRACEBAG_LOG_MAX_TOTAL_BYTES"], 1_073_741_824, min: 1_048_576),
            LogMaxBytesPerContainer = ParseLong(configuration["TRACEBAG_LOG_MAX_BYTES_PER_CONTAINER"], 268_435_456, min: 1_048_576),
            LogRetentionDeleteBatchSize = ParseInt(configuration["TRACEBAG_LOG_RETENTION_DELETE_BATCH_SIZE"], 1_000, min: 100, max: 10_000),
            LogRetentionScanSeconds = ParseInt(configuration["TRACEBAG_LOG_RETENTION_SCAN_SECONDS"], 60, min: 5, max: 3_600),
            LogMaxLineBytes = ParseInt(configuration["TRACEBAG_LOG_MAX_LINE_BYTES"], 262_144, min: 4_096, max: 1_048_576),
            DiagnosticJobMaxActiveGlobal = ParseInt(configuration["TRACEBAG_DIAGNOSTIC_JOB_MAX_ACTIVE_GLOBAL"], 2, min: 1, max: 20),
            DiagnosticJobDailyLimit = ParseInt(configuration["TRACEBAG_DIAGNOSTIC_JOB_DAILY_LIMIT"], 25, min: 1, max: 1000),
            DiagnosticJobMaxDurationSeconds = ParseInt(configuration["TRACEBAG_DIAGNOSTIC_JOB_MAX_DURATION_SECONDS"], 600, min: 30, max: 3600),
            FullDumpEnabled = ParseBool(configuration["TRACEBAG_FULL_DUMP_ENABLED"], defaultValue: false),
            AnalysisMaxTraceBytes = ParseLong(configuration["TRACEBAG_ANALYSIS_MAX_TRACE_BYTES"], 536_870_912, min: 1_048_576),
            AnalysisMaxStackBytes = ParseLong(configuration["TRACEBAG_ANALYSIS_MAX_STACK_BYTES"], 8_388_608, min: 65_536),
            AnalysisMaxEvents = ParseInt(configuration["TRACEBAG_ANALYSIS_MAX_EVENTS"], 2_000_000, min: 10_000, max: 10_000_000)
        };
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private static (string Key, string Value) SplitLabel(string raw)
    {
        var parts = raw.Split('=', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]))
        {
            throw new InvalidOperationException($"Invalid label expression: {raw}");
        }

        return (parts[0], parts[1]);
    }

    private static (string Key, string Value)? SplitOptionalLabel(string? raw)
    {
        return string.IsNullOrWhiteSpace(raw) ? null : SplitLabel(raw);
    }

    private static string[] ParseList(string? raw)
    {
        return string.IsNullOrWhiteSpace(raw)
            ? []
            : raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static bool ParseBool(string? value, bool defaultValue)
    {
        return bool.TryParse(value, out var result) ? result : defaultValue;
    }

    private static int ParseInt(string? value, int defaultValue, int min, int max)
    {
        if (!int.TryParse(value, out var result))
        {
            result = defaultValue;
        }

        return Math.Clamp(result, min, max);
    }

    private static long ParseLong(string? value, long defaultValue, long min, long max = long.MaxValue)
    {
        if (!long.TryParse(value, out var result))
        {
            result = defaultValue;
        }

        return Math.Clamp(result, min, max);
    }
}
