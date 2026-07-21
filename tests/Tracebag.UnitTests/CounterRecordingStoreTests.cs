using Tracebag.Api.Auth;
using Tracebag.Api.Data;
using Tracebag.Api.Diagnostics;
using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace Tracebag.UnitTests;

public sealed class CounterRecordingStoreTests
{
    [Fact]
    public async Task StoresSamplesAndReturnsSeries()
    {
        var options = new DbContextOptionsBuilder<TracebagDbContext>()
            .UseInMemoryDatabase($"tracebag-recordings-{Guid.NewGuid():N}")
            .Options;
        await using var db = new TracebagDbContext(options);
        var factory = new TestDbContextFactory(options);
        var store = new CounterRecordingStore(TestOptions(), factory);

        await store.ReserveAsync(
            "rec-1",
            "container-1",
            "api",
            1,
            "runtime",
            ["System.Runtime"],
            5,
            3600,
            "test run",
            "admin",
            Runner(),
            CancellationToken.None);
        await store.MarkRunningAsync("rec-1", "runner-1", CancellationToken.None);
        await store.AddSamplesAsync(
            "rec-1",
            [
                new CounterSample(DateTimeOffset.Parse("2026-06-28T21:00:05Z", CultureInfo.InvariantCulture), "System.Runtime", "cpu-usage", "Metric", 12.5),
                new CounterSample(DateTimeOffset.Parse("2026-06-28T21:00:10Z", CultureInfo.InvariantCulture), "System.Runtime", "cpu-usage", "Metric", 20)
            ],
            CancellationToken.None);

        var detail = await store.GetDetailAsync("rec-1", CancellationToken.None);
        var samples = await store.GetSamplesAsync("rec-1", null, null, "raw", CancellationToken.None);

        Assert.Equal("running", detail.Recording.Status);
        Assert.Equal(2, detail.Recording.SampleCount);
        Assert.Single(detail.Series);
        Assert.Single(samples.Series);
        Assert.Equal(2, samples.Series[0].Points.Count);
        Assert.Equal(12.5, samples.Series[0].Summary.Minimum);
        Assert.Equal(20, samples.Series[0].Summary.Maximum);

        var rollups = await store.GetSamplesAsync("rec-1", null, null, "1m", CancellationToken.None);
        Assert.Single(rollups.Series[0].Points);
        Assert.Equal(16.25, rollups.Series[0].Points[0].Value);
        Assert.Equal(2, rollups.Series[0].Points[0].Count);
    }

    [Fact]
    public async Task MarksActiveRecordingsInterrupted()
    {
        var options = new DbContextOptionsBuilder<TracebagDbContext>()
            .UseInMemoryDatabase($"tracebag-recordings-{Guid.NewGuid():N}")
            .Options;
        var store = new CounterRecordingStore(TestOptions(), new TestDbContextFactory(options));

        await store.ReserveAsync("rec-1", "container-1", "api", 1, "runtime", ["System.Runtime"], 5, 3600, null, "admin", Runner(), CancellationToken.None);
        await store.MarkRunningAsync("rec-1", "runner-1", CancellationToken.None);

        var interrupted = await store.MarkActiveInterruptedAsync(CancellationToken.None);
        var detail = await store.GetDetailAsync("rec-1", CancellationToken.None);

        Assert.Single(interrupted);
        Assert.Equal("failed", detail.Recording.Status);
        Assert.Equal("interrupted", detail.Recording.StopReason);
    }

    [Fact]
    public async Task ReservationEnforcesPerTargetAndGlobalLimits()
    {
        var options = new DbContextOptionsBuilder<TracebagDbContext>()
            .UseInMemoryDatabase($"tracebag-recordings-{Guid.NewGuid():N}")
            .Options;
        var configured = TestOptions() with { CounterRecordingMaxActiveGlobal = 1 };
        var store = new CounterRecordingStore(configured, new TestDbContextFactory(options));

        await store.ReserveAsync("rec-1", "container-1", "api", 1, "runtime", ["System.Runtime"], 5, 3600, null, "admin", Runner(), CancellationToken.None);

        var perTarget = await Assert.ThrowsAsync<Tracebag.Api.Security.TracebagException>(() =>
            store.ReserveAsync("rec-2", "container-1", "api", 1, "runtime", ["System.Runtime"], 5, 3600, null, "admin", Runner(), CancellationToken.None));
        var global = await Assert.ThrowsAsync<Tracebag.Api.Security.TracebagException>(() =>
            store.ReserveAsync("rec-3", "container-2", "worker", 1, "runtime", ["System.Runtime"], 5, 3600, null, "admin", Runner(), CancellationToken.None));

        Assert.Equal("counter_recording_already_running", perTarget.Code);
        Assert.Equal("counter_recording_global_limit", global.Code);
    }

    [Fact]
    public async Task UpdatesNameAndNotes()
    {
        var options = new DbContextOptionsBuilder<TracebagDbContext>()
            .UseInMemoryDatabase($"tracebag-recordings-{Guid.NewGuid():N}")
            .Options;
        var store = new CounterRecordingStore(TestOptions(), new TestDbContextFactory(options));
        await store.ReserveAsync("rec-1", "container-1", "api", 1, "runtime", ["System.Runtime"], 5, 3600, null, "admin", Runner(), CancellationToken.None);

        var updated = await store.UpdateMetadataAsync("rec-1", "Pressure test", "Triggered during checkout load.", CancellationToken.None);

        Assert.Equal("Pressure test", updated.Name);
        Assert.Equal("Triggered during checkout load.", updated.Notes);
        Assert.Equal(8, updated.RuntimeMajor);
    }

    private static DiagnosticRunnerSelection Runner() => new(8, "diag:8", "8.0.547301", true);

    private static TracebagOptions TestOptions()
    {
        return new TracebagOptions
        {
            Stage = "test",
            AuthEnabled = true,
            AdminUser = "admin",
            AdminPasswordHash = "hash",
            AllowedLabelKey = "tracebag.enabled",
            AllowedLabelValue = "true",
            EnvironmentLabelKey = null,
            EnvironmentLabelValue = null,
            ArtifactDir = "/tmp/artifacts",
            DataDir = "/tmp/data",
            ArtifactVolume = "artifacts",
            DiagnosticImage = "diag",
            PublicBaseUrl = "http://localhost:9090",
            RestartEnabled = false,
            CounterMaxSeconds = 600,
            ArtifactRetentionHours = 24,
            ArtifactMaxCount = 20,
            ArtifactMaxTotalBytes = 1024 * 1024,
            DatabaseUrl = "in-memory"
        };
    }

    private sealed class TestDbContextFactory(DbContextOptions<TracebagDbContext> options) : IDbContextFactory<TracebagDbContext>
    {
        public TracebagDbContext CreateDbContext()
        {
            return new TracebagDbContext(options);
        }

        public Task<TracebagDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(CreateDbContext());
        }
    }
}
