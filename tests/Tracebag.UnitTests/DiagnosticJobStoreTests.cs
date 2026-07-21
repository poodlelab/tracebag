using Microsoft.EntityFrameworkCore;
using Tracebag.Api.Auth;
using Tracebag.Api.Data;
using Tracebag.Api.Diagnostics;
using Tracebag.Api.Security;

namespace Tracebag.UnitTests;

public sealed class DiagnosticJobStoreTests
{
    [Fact]
    public async Task PersistsTransitionsEventsAndIdempotentCancellation()
    {
        var store = CreateStore(out _);
        await store.ReserveAsync(Reservation("job-1", "target-1", "key", "fingerprint"), CancellationToken.None);
        await store.TransitionAsync("job-1", "validating", 10, "validated");
        await store.TransitionAsync("job-1", "starting", 20, "starting");
        await store.TransitionAsync("job-1", "running", 35, "running", runnerContainerId: "runner-1");

        var first = await store.RequestCancellationAsync("job-1", CancellationToken.None);
        var second = await store.RequestCancellationAsync("job-1", CancellationToken.None);
        await store.TransitionAsync("job-1", "cancelled", 0, "cancelled");
        var events = await store.GetEventsAsync("job-1", 0, CancellationToken.None);

        Assert.Equal("stopping", first.Status);
        Assert.Equal("stopping", second.Status);
        Assert.Equal("cancelled", (await store.GetAsync("job-1", CancellationToken.None)).Status);
        Assert.Equal(6, events.Count);
        Assert.Equal("completed", events[^1].Type);
    }

    [Fact]
    public async Task EnforcesPerTargetGlobalDailyAndIdempotencyLimits()
    {
        var store = CreateStore(out _, Options() with { DiagnosticJobMaxActiveGlobal = 1, DiagnosticJobDailyLimit = 2 });
        var first = await store.ReserveAsync(Reservation("job-1", "target-1", "key", "fingerprint"), CancellationToken.None);
        var duplicate = await store.ReserveAsync(Reservation("job-2", "target-1", "key", "fingerprint"), CancellationToken.None);
        Assert.Equal(first.Id, duplicate.Id);
        Assert.Equal("idempotency_key_reused", (await Assert.ThrowsAsync<TracebagException>(() => store.ReserveAsync(Reservation("job-3", "target-1", "key", "different"), CancellationToken.None))).Code);
        Assert.Equal("diagnostic_target_busy", (await Assert.ThrowsAsync<TracebagException>(() => store.ReserveAsync(Reservation("job-4", "target-1", null, "four"), CancellationToken.None))).Code);
        Assert.Equal("diagnostic_global_limit", (await Assert.ThrowsAsync<TracebagException>(() => store.ReserveAsync(Reservation("job-5", "target-2", null, "five"), CancellationToken.None))).Code);

        await store.TransitionAsync("job-1", "failed", 99, "failed");
        await store.ReserveAsync(Reservation("job-6", "target-2", null, "six"), CancellationToken.None);
        await store.TransitionAsync("job-6", "failed", 99, "failed");
        Assert.Equal("diagnostic_daily_limit", (await Assert.ThrowsAsync<TracebagException>(() => store.ReserveAsync(Reservation("job-7", "target-3", null, "seven"), CancellationToken.None))).Code);
    }

    [Fact]
    public async Task RestartMarksEveryActiveStateFailed()
    {
        var store = CreateStore(out _);
        await store.ReserveAsync(Reservation("job-1", "target-1", null, "one"), CancellationToken.None);

        var interrupted = await store.MarkActiveInterruptedAsync(CancellationToken.None);

        Assert.Single(interrupted);
        Assert.Equal("tracebag_restarted", interrupted[0].ErrorCode);
        Assert.Equal("failed", interrupted[0].Status);
    }

    private static DiagnosticJobStore CreateStore(out TestFactory factory, TracebagOptions? options = null)
    {
        var dbOptions = new DbContextOptionsBuilder<TracebagDbContext>()
            .UseInMemoryDatabase($"diagnostic-jobs-{Guid.NewGuid():N}").Options;
        factory = new TestFactory(dbOptions);
        return new DiagnosticJobStore(factory, options ?? Options());
    }

    private static DiagnosticJobReservation Reservation(string id, string target, string? key, string fingerprint)
    {
        var now = DateTimeOffset.UtcNow;
        return new(id, target, "api", "docker", 10, "cpu-trace", now, now.AddMinutes(2), "admin", key, fingerprint, "{\"processId\":10}", new DiagnosticRunnerSelection(8, "diag:8", "8.0", true));
    }

    private static TracebagOptions Options() => new()
    {
        Stage = "test",
        AuthEnabled = true,
        AdminUser = "admin",
        AdminPasswordHash = "hash",
        AllowedLabelKey = "tracebag.enabled",
        AllowedLabelValue = "true",
        ArtifactDir = "/tmp/artifacts",
        DataDir = "/tmp/data",
        ArtifactVolume = "artifacts",
        DiagnosticImage = "diag",
        PublicBaseUrl = "http://localhost",
        RestartEnabled = false,
        CounterMaxSeconds = 600,
        ArtifactRetentionHours = 24,
        ArtifactMaxCount = 20,
        ArtifactMaxTotalBytes = 1024 * 1024,
        DiagnosticJobMaxActiveGlobal = 2,
        DiagnosticJobDailyLimit = 25
    };

    private sealed class TestFactory(DbContextOptions<TracebagDbContext> options) : IDbContextFactory<TracebagDbContext>
    {
        public TracebagDbContext CreateDbContext() => new(options);
        public Task<TracebagDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }
}
