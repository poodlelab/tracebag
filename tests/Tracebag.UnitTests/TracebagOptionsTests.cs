using Microsoft.Extensions.Configuration;
using Tracebag.Api.Auth;

namespace Tracebag.UnitTests;

public sealed class TracebagOptionsTests
{
    [Fact]
    public void UsesStandaloneTracebagDefaults()
    {
        var configuration = new ConfigurationBuilder().Build();

        var options = TracebagOptions.FromConfiguration(configuration);

        Assert.Equal("local", options.Stage);
        Assert.Equal("tracebag.enabled", options.AllowedLabelKey);
        Assert.Equal("true", options.AllowedLabelValue);
        Assert.Null(options.EnvironmentLabelKey);
        Assert.Equal("http://localhost:9090", options.PublicBaseUrl);
        Assert.Equal("tracebag-runner-dotnet-8:dev", options.DiagnosticImage);
        Assert.True(options.LogIngestionEnabled);
        Assert.Equal(5_000, options.LogChannelCapacity);
        Assert.Equal(7, options.LogRetentionDays);
        Assert.Equal(1_073_741_824, options.LogMaxTotalBytes);
        Assert.Equal(2, options.DiagnosticJobMaxActiveGlobal);
        Assert.Equal(25, options.DiagnosticJobDailyLimit);
        Assert.Equal(600, options.DiagnosticJobMaxDurationSeconds);
        Assert.False(options.FullDumpEnabled);
        Assert.Equal(5, options.AuthLoginPermitLimit);
        Assert.Equal(60, options.AuthLoginWindowSeconds);
        Assert.Empty(options.TrustedProxies);
        Assert.Equal(30, options.AuditRetentionDays);
        Assert.Equal(100_000, options.AuditMaxEvents);
        Assert.Equal(1_000, options.AuditRetentionDeleteBatchSize);
        Assert.Equal(300, options.AuditRetentionScanSeconds);
        Assert.Equal(30, options.DiagnosticJobRetentionDays);
        Assert.Equal(100, options.DiagnosticJobRetentionDeleteBatchSize);
        Assert.Equal(300, options.DurableRetentionScanSeconds);
        Assert.Equal(200, options.IncidentMaxCount);
        Assert.Equal(1_073_741_824, options.RunnerMemoryLimitBytes);
        Assert.Equal(1_000, options.RunnerCpuLimitMillicores);
        Assert.Equal(128, options.RunnerPidsLimit);
    }

    [Fact]
    public void BoundsAuthenticationAndAuditConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TRACEBAG_AUTH_LOGIN_PERMIT_LIMIT"] = "1",
                ["TRACEBAG_AUTH_LOGIN_WINDOW_SECONDS"] = "99999",
                ["TRACEBAG_AUDIT_RETENTION_DAYS"] = "99999",
                ["TRACEBAG_AUDIT_MAX_EVENTS"] = "10",
                ["TRACEBAG_AUDIT_RETENTION_DELETE_BATCH_SIZE"] = "2",
                ["TRACEBAG_AUDIT_RETENTION_SCAN_SECONDS"] = "1",
                ["TRACEBAG_DIAGNOSTIC_JOB_RETENTION_DAYS"] = "99999",
                ["TRACEBAG_DIAGNOSTIC_JOB_RETENTION_DELETE_BATCH_SIZE"] = "1",
                ["TRACEBAG_DURABLE_RETENTION_SCAN_SECONDS"] = "99999",
                ["TRACEBAG_INCIDENT_MAX_COUNT"] = "1",
                ["TRACEBAG_RUNNER_MEMORY_LIMIT_BYTES"] = "1",
                ["TRACEBAG_RUNNER_CPU_LIMIT_MILLICORES"] = "99999",
                ["TRACEBAG_RUNNER_PIDS_LIMIT"] = "1",
                ["TRACEBAG_TRUSTED_PROXIES"] = " 127.0.0.1, 10.20.0.0/16 "
            })
            .Build();

        var options = TracebagOptions.FromConfiguration(configuration);

        Assert.Equal(2, options.AuthLoginPermitLimit);
        Assert.Equal(3_600, options.AuthLoginWindowSeconds);
        Assert.Equal(3_650, options.AuditRetentionDays);
        Assert.Equal(1_000, options.AuditMaxEvents);
        Assert.Equal(100, options.AuditRetentionDeleteBatchSize);
        Assert.Equal(60, options.AuditRetentionScanSeconds);
        Assert.Equal(3_650, options.DiagnosticJobRetentionDays);
        Assert.Equal(10, options.DiagnosticJobRetentionDeleteBatchSize);
        Assert.Equal(86_400, options.DurableRetentionScanSeconds);
        Assert.Equal(10, options.IncidentMaxCount);
        Assert.Equal(134_217_728, options.RunnerMemoryLimitBytes);
        Assert.Equal(8_000, options.RunnerCpuLimitMillicores);
        Assert.Equal(32, options.RunnerPidsLimit);
        Assert.Equal(["127.0.0.1", "10.20.0.0/16"], options.TrustedProxies);
    }

    [Fact]
    public void BoundsPersistentLogConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TRACEBAG_LOG_CHANNEL_CAPACITY"] = "1",
                ["TRACEBAG_LOG_BATCH_SIZE"] = "99999",
                ["TRACEBAG_LOG_RETENTION_DAYS"] = "999",
                ["TRACEBAG_LOG_MAX_LINE_BYTES"] = "2"
            })
            .Build();

        var options = TracebagOptions.FromConfiguration(configuration);

        Assert.Equal(100, options.LogChannelCapacity);
        Assert.Equal(2_000, options.LogBatchSize);
        Assert.Equal(90, options.LogRetentionDays);
        Assert.Equal(4_096, options.LogMaxLineBytes);
    }

    [Fact]
    public void ParsesOptionalEnvironmentLabel()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TRACEBAG_ENVIRONMENT_LABEL"] = "tracebag.environment=prod"
            })
            .Build();

        var options = TracebagOptions.FromConfiguration(configuration);

        Assert.Equal("tracebag.environment", options.EnvironmentLabelKey);
        Assert.Equal("prod", options.EnvironmentLabelValue);
    }

    [Fact]
    public void RejectsInvalidAllowedLabelExpression()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TRACEBAG_ALLOWED_LABEL"] = "invalid"
            })
            .Build();

        Assert.Throws<InvalidOperationException>(() => TracebagOptions.FromConfiguration(configuration));
    }

    [Fact]
    public void RejectsMissingPasswordHashWhenAuthenticationIsEnabled()
    {
        var options = TracebagOptions.FromConfiguration(new ConfigurationBuilder().Build());

        var exception = Assert.Throws<InvalidOperationException>(options.ValidateForStartup);

        Assert.Contains("TRACEBAG_ADMIN_PASSWORD_HASH", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsDisabledAuthenticationOutsideLocalStage()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TRACEBAG_STAGE"] = "production",
                ["TRACEBAG_AUTH_ENABLED"] = "false"
            })
            .Build();
        var options = TracebagOptions.FromConfiguration(configuration);

        Assert.Throws<InvalidOperationException>(options.ValidateForStartup);
    }
}
