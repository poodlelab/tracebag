using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Tracebag.Api.Auth;
using Tracebag.Api.Data;
using Tracebag.Api.Diagnostics;
using Tracebag.Api.Retention;

namespace Tracebag.UnitTests;

public sealed class DurableRetentionTests
{
    [Fact]
    public async Task DeletesOnlyExpiredUnreferencedTerminalJobsAndIsIdempotent()
    {
        var factory = Factory();
        var options = Options() with
        {
            DiagnosticJobRetentionDays = 30,
            DiagnosticJobRetentionDeleteBatchSize = 10
        };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Incidents.Add(Incident("incident-protect"));
            db.IncidentEvidence.Add(Evidence("incident-protect", "evidence-job", "diagnostic-artifact", "job-protected"));
            db.DiagnosticJobs.AddRange(
                Job("job-expired", "completed", DateTimeOffset.UtcNow.AddDays(-40)),
                Job("job-protected", "failed", DateTimeOffset.UtcNow.AddDays(-40)),
                Job("job-active", "running", DateTimeOffset.UtcNow.AddDays(-40)),
                Job("job-recent", "completed", DateTimeOffset.UtcNow.AddDays(-1)));
            db.DiagnosticJobEvents.AddRange(
                JobEvent("job-expired"),
                JobEvent("job-protected"));
            await db.SaveChangesAsync();
        }
        var retention = new DurableRetentionStore(factory, options);

        var first = await retention.ApplyAsync(CancellationToken.None);
        var second = await retention.ApplyAsync(CancellationToken.None);

        Assert.Equal(1, first.DeletedJobs);
        Assert.Equal(0, second.DeletedJobs);
        await using var result = await factory.CreateDbContextAsync();
        Assert.False(await result.DiagnosticJobs.AnyAsync(job => job.Id == "job-expired"));
        Assert.False(await result.DiagnosticJobEvents.AnyAsync(item => item.JobId == "job-expired"));
        Assert.True(await result.DiagnosticJobs.AnyAsync(job => job.Id == "job-protected"));
        Assert.True(await result.DiagnosticJobs.AnyAsync(job => job.Id == "job-active"));
        Assert.True(await result.DiagnosticJobs.AnyAsync(job => job.Id == "job-recent"));

        var status = await retention.StatusAsync(CancellationToken.None);
        Assert.Equal(1, status.ExpiredJobsProtectedByIncidents);
        Assert.Equal(0, status.ExpiredJobsEligible);
        Assert.Equal(1, status.Incidents);
    }

    [Fact]
    public async Task IncidentReferenceProtectsExpiredRecordingUntilIncidentIsRemoved()
    {
        var factory = Factory();
        var options = Options() with { CounterRecordingRetentionDays = 7 };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Incidents.Add(Incident("incident-recording"));
            db.IncidentEvidence.Add(Evidence("incident-recording", "evidence-recording", "counter-window", "recording-protected"));
            db.CounterRecordingSessions.Add(Recording("recording-protected", DateTimeOffset.UtcNow.AddDays(-10)));
            await db.SaveChangesAsync();
        }
        using var recordings = new CounterRecordingStore(options, factory);

        await recordings.ApplyRetentionAsync(CancellationToken.None);
        Assert.True(await recordings.IsReferencedByIncidentAsync("recording-protected", CancellationToken.None));
        await using (var db = await factory.CreateDbContextAsync())
        {
            Assert.True(await db.CounterRecordingSessions.AnyAsync(item => item.Id == "recording-protected"));
            db.IncidentEvidence.RemoveRange(await db.IncidentEvidence.ToArrayAsync());
            db.Incidents.Remove(await db.Incidents.SingleAsync());
            await db.SaveChangesAsync();
        }

        await recordings.ApplyRetentionAsync(CancellationToken.None);

        await using var result = await factory.CreateDbContextAsync();
        Assert.False(await result.CounterRecordingSessions.AnyAsync(item => item.Id == "recording-protected"));
    }

    private static TestFactory Factory()
    {
        var options = new DbContextOptionsBuilder<TracebagDbContext>()
            .UseInMemoryDatabase($"durable-retention-{Guid.NewGuid():N}")
            .Options;
        return new TestFactory(options);
    }

    private static TracebagOptions Options() => TracebagOptions.FromConfiguration(
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TRACEBAG_AUTH_ENABLED"] = "false"
            })
            .Build());

    private static DiagnosticJobRecord Job(string id, string status, DateTimeOffset createdAt) => new()
    {
        Id = id,
        ContainerId = "target",
        ContainerName = "Target",
        DockerId = "docker",
        ProcessId = 1,
        Profile = "stack-snapshot",
        Status = status,
        Progress = status == "running" ? 50 : 100,
        CreatedAt = createdAt,
        CompletedAt = status == "running" ? null : createdAt.AddMinutes(1),
        DeadlineAt = createdAt.AddMinutes(10),
        CreatedBy = "admin",
        RequestFingerprint = id,
        InputsJson = "{}",
        RuntimeMajor = 8,
        RunnerImage = "runner",
        ToolVersion = "test"
    };

    private static DiagnosticJobEventRecord JobEvent(string jobId) => new()
    {
        JobId = jobId,
        Timestamp = DateTimeOffset.UtcNow.AddDays(-40),
        Type = "completed",
        Status = "completed",
        Progress = 100,
        Message = "Finished."
    };

    private static IncidentRecord Incident(string id) => new()
    {
        Id = id,
        ContainerId = "target",
        ContainerName = "Target",
        DockerId = "docker",
        ProcessId = 1,
        Title = id,
        Profile = "high-cpu",
        Status = "closed",
        Progress = 100,
        CreatedBy = "admin",
        CreatedAt = DateTimeOffset.UtcNow.AddDays(-40),
        WindowStart = DateTimeOffset.UtcNow.AddDays(-40),
        CompletedAt = DateTimeOffset.UtcNow.AddDays(-40),
        CaptureOptionsJson = "{}"
    };

    private static IncidentEvidenceRecord Evidence(string incidentId, string id, string kind, string sourceId) => new()
    {
        Id = id,
        IncidentId = incidentId,
        Kind = kind,
        Title = id,
        CapturedAt = DateTimeOffset.UtcNow.AddDays(-40),
        SourceId = sourceId,
        SummaryJson = "{}",
        PayloadJson = "{}",
        RedactionStatus = "not-required"
    };

    private static CounterRecordingSessionRecord Recording(string id, DateTimeOffset startedAt) => new()
    {
        Id = id,
        ContainerId = "target",
        ContainerName = "Target",
        ProcessId = 1,
        Preset = "cpu",
        IntervalSeconds = 5,
        MaxDurationSeconds = 60,
        StartedAt = startedAt,
        StoppedAt = startedAt.AddMinutes(1),
        Status = "completed",
        CreatedBy = "admin",
        RuntimeMajor = 8,
        RunnerImage = "runner",
        ToolVersion = "test"
    };

    private sealed class TestFactory(DbContextOptions<TracebagDbContext> options) : IDbContextFactory<TracebagDbContext>
    {
        public TracebagDbContext CreateDbContext() => new(options);
        public Task<TracebagDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
