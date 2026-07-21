using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tracebag.Api.Analysis;
using Tracebag.Api.Artifacts;
using Tracebag.Api.Audit;
using Tracebag.Api.Auth;
using Tracebag.Api.Data;
using Tracebag.Api.Models;

namespace Tracebag.UnitTests;

public sealed class LocalAnalysisTests
{
    [Fact]
    public void StackGroupsNormalizeAddressesAndAsyncStateMachines()
    {
        const string snapshot = """
            Thread (0x1):
              at Demo.Worker+<Run>d__12.MoveNext() +0x1a
              at System.Threading.Monitor.Enter(System.Object) [0xabc]
            Thread (0x2):
              at Demo.Worker+<Run>d__99.MoveNext() +0xff
              at System.Threading.Monitor.Enter(System.Object) [0xdef]
            """;

        IReadOnlyList<StackGroup> groups = StackSnapshotAnalyzer.Group(snapshot);

        StackGroup group = Assert.Single(groups);
        Assert.Equal(2, group.Count);
        Assert.Contains("Demo.Worker.Run() [async]", group.Frames);
        Assert.Contains("System.Threading.Monitor.Enter()", group.Frames);
    }

    [Fact]
    public void StackGroupsCurrentDotnetStackAssemblyQualifiedFormat()
    {
        const string snapshot = """
            Thread (0x1):
              [Native Frames]
              System.Private.CoreLib!System.Threading.Monitor.Wait(class System.Object,int32)
              System.Private.CoreLib!System.Threading.Tasks.Task.InternalWaitCore(int32,value class System.Threading.CancellationToken)
              Tracebag.Demo.Api!Program+<<Main>$>d__0.MoveNext()

            Thread (0x2D):
              System.Private.CoreLib!System.Threading.LowLevelLifoSemaphore.Wait(int32,bool)
              System.Private.CoreLib!System.Threading.PortableThreadPool+WorkerThread.WorkerThreadStart()
            """;

        IReadOnlyList<StackGroup> groups = StackSnapshotAnalyzer.Group(snapshot);

        Assert.Equal(2, groups.Count);
        Assert.Equal(2, groups.Sum(group => group.Count));
        Assert.Contains(groups.SelectMany(group => group.Frames), frame => frame.Contains("System.Threading.Monitor.Wait()", StringComparison.Ordinal));
        Assert.DoesNotContain(groups.SelectMany(group => group.Frames), frame => frame == "[Native Frames]");
    }

    [Fact]
    public void TraceAccumulatorProducesBoundedSignalCodes()
    {
        var accumulator = new TraceAccumulator(100);
        accumulator.Accept("Microsoft-DotNETCore-SampleProfiler", "ThreadSample", ["Demo.Cpu.Burn()", "System.Threading.Thread.Start()"]);
        accumulator.Accept("Microsoft-Windows-DotNETRuntime", "ContentionStop", []);
        accumulator.Accept("Microsoft-Windows-DotNETRuntime", "ExceptionThrown", [], "System.TimeoutException");
        accumulator.Accept("Microsoft-Windows-DotNETRuntime", "GCSuspendEE/Begin", [], timestampMilliseconds: 10);
        accumulator.Accept("Microsoft-Windows-DotNETRuntime", "GCRestartEE/End", [], timestampMilliseconds: 14.5);
        accumulator.Accept("Microsoft-Windows-DotNETRuntime", "ThreadPoolWorkerThreadStart", []);

        IReadOnlyList<AnalysisObservation> observations = accumulator.Build("evidence-1");

        Assert.Equal(["contention-events", "cpu-hot-paths", "exception-events", "gc-pauses", "thread-pool-events"], observations.Select(x => x.Code).Order().ToArray());
        Assert.All(observations, x => Assert.Equal(["evidence-1"], x.EvidenceIds));
    }

    [Fact]
    public void AnalysisEnvelopeIsVersionedLocalOnlyAndRoundTrips()
    {
        var envelope = new AnalysisEnvelope(1, "tracebag-local/1", "inc-1", DateTimeOffset.UnixEpoch,
            new AnalysisWindow(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddMinutes(1)),
            [new AnalysisSource("evidence-1", "logs", "Pinned logs", null)],
            [new AnalysisComponent("signals", "completed", 4, 1, null)],
            [new AnalysisObservation("obs-1", "signals", "log-errors", "warning", "high", "Errors", "Bounded errors", ["evidence-1"], new { count = 2 })],
            [], [new AnalysisLimitation("bounded", "Only the incident window is analyzed.", "evidence-1")],
            new AnalysisDisclosure(true, false, false));

        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        string json = JsonSerializer.Serialize(envelope, jsonOptions);
        AnalysisEnvelope? restored = JsonSerializer.Deserialize<AnalysisEnvelope>(json, jsonOptions);

        Assert.NotNull(restored);
        Assert.Equal(1, restored.SchemaVersion);
        Assert.True(restored.Disclosure.LocalOnly);
        Assert.False(restored.Disclosure.ExternalProvidersUsed);
        Assert.Equal("evidence-1", Assert.Single(restored.Observations).EvidenceIds.Single());
    }

    [Fact]
    public async Task CorruptTraceFailsIndependentlyAndPreservesStackFindings()
    {
        string root = Path.Combine(Path.GetTempPath(), $"tracebag-analysis-test-{Guid.NewGuid():N}");
        string artifactDir = Path.Combine(root, "artifacts");
        string dataDir = Path.Combine(root, "data");
        Directory.CreateDirectory(artifactDir);
        Directory.CreateDirectory(dataDir);
        await File.WriteAllTextAsync(Path.Combine(artifactDir, "stack.txt"), "Thread (0x1):\n  at Demo.Work.Run() +0x1\n");
        await File.WriteAllTextAsync(Path.Combine(artifactDir, "bad.nettrace"), "not an EventPipe trace");
        try
        {
            DbContextOptions<TracebagDbContext> dbOptions = new DbContextOptionsBuilder<TracebagDbContext>().UseInMemoryDatabase($"analysis-{Guid.NewGuid():N}").Options;
            var factory = new TestFactory(dbOptions);
            await using (TracebagDbContext db = factory.CreateDbContext())
            {
                DateTimeOffset now = DateTimeOffset.UtcNow;
                db.Incidents.Add(new IncidentRecord { Id = "incident-1", ContainerId = "target", ContainerName = "api", DockerId = "docker", ProcessId = 1, Title = "CPU", Profile = "high-cpu", Status = "ready", CreatedBy = "admin", CreatedAt = now, WindowStart = now.AddMinutes(-1), WindowEnd = now, CompletedAt = now, CaptureOptionsJson = "{}" });
                db.Artifacts.AddRange(
                    new ArtifactRecord { Id = "artifact-stack", ContainerId = "target", ContainerName = "api", Type = "stack-snapshot", FileName = "stack.txt", CreatedAt = now, Size = 42, CreatedBy = "admin", ExpiresAt = now.AddHours(1) },
                    new ArtifactRecord { Id = "artifact-trace", ContainerId = "target", ContainerName = "api", Type = "cpu-trace", FileName = "bad.nettrace", CreatedAt = now, Size = 22, CreatedBy = "admin", ExpiresAt = now.AddHours(1) });
                db.IncidentEvidence.AddRange(
                    new IncidentEvidenceRecord { Id = "evidence-stack", IncidentId = "incident-1", Kind = "diagnostic-artifact", Title = "stack-snapshot artifact", CapturedAt = now, ArtifactId = "artifact-stack", SummaryJson = "{}", PayloadJson = "{}", RedactionStatus = "not-redacted" },
                    new IncidentEvidenceRecord { Id = "evidence-trace", IncidentId = "incident-1", Kind = "diagnostic-artifact", Title = "cpu-trace artifact", CapturedAt = now, ArtifactId = "artifact-trace", SummaryJson = "{}", PayloadJson = "{}", RedactionStatus = "not-redacted" });
                await db.SaveChangesAsync();
            }

            TracebagOptions options = TestOptions(artifactDir, dataDir);
            using var store = new ArtifactStore(options, factory);
            using var audit = new AuditLog(options, factory);
            var service = new LocalAnalysisService(factory, store, new StackSnapshotAnalyzer(options), new NetTraceAnalyzer(options), audit);

            AnalysisRunDto run = await service.AnalyzeAsync("incident-1", "admin", CancellationToken.None);

            Assert.Equal("partial", run.Status);
            Assert.NotNull(run.Envelope);
            Assert.Contains(run.Envelope.Components, x => x.Name == "stack" && x.Status == "completed");
            Assert.Contains(run.Envelope.Components, x => x.Name == "trace" && x.Status == "failed");
            Assert.Contains(run.Envelope.Observations, x => x.Code == "grouped-stacks" && x.EvidenceIds.SequenceEqual(["evidence-stack"]));
            Assert.Contains(run.Envelope.Limitations, x => x.Code == "trace-analysis-failed" && x.EvidenceId == "evidence-trace");
            await using TracebagDbContext verification = factory.CreateDbContext();
            Assert.Equal("ready", (await verification.Incidents.FindAsync("incident-1"))!.Status);
            Assert.Equal(2, await verification.IncidentEvidence.CountAsync());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static TracebagOptions TestOptions(string artifactDir, string dataDir) => new()
    {
        Stage = "test", AuthEnabled = true, AdminUser = "admin", AdminPasswordHash = "hash",
        AllowedLabelKey = "tracebag.enabled", AllowedLabelValue = "true", ArtifactDir = artifactDir,
        DataDir = dataDir, ArtifactVolume = "artifacts", DiagnosticImage = "runner", PublicBaseUrl = "http://localhost",
        RestartEnabled = false, CounterMaxSeconds = 600, ArtifactRetentionHours = 24,
        ArtifactMaxCount = 20, ArtifactMaxTotalBytes = 1024 * 1024
    };

    private sealed class TestFactory(DbContextOptions<TracebagDbContext> options) : IDbContextFactory<TracebagDbContext>
    {
        public TracebagDbContext CreateDbContext() => new(options);
        public Task<TracebagDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }
}
