using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tracebag.Api.Artifacts;
using Tracebag.Api.Auth;
using Tracebag.Api.Data;
using Tracebag.Api.Security;

namespace Tracebag.UnitTests;

public sealed class ArtifactStoreTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task RetentionDeletesExpiredArtifactsAndFiles()
    {
        string root = Path.Combine(Path.GetTempPath(), $"tracebag-tests-{Guid.NewGuid():N}");
        string artifactDir = Path.Combine(root, "artifacts");
        string dataDir = Path.Combine(root, "data");
        Directory.CreateDirectory(artifactDir);
        Directory.CreateDirectory(dataDir);
        string fileName = "expired.nettrace";
        await File.WriteAllTextAsync(Path.Combine(artifactDir, fileName), "trace");
        var expired = new ArtifactMetadata(
            "trace-1",
            "container",
            "api",
            "trace",
            fileName,
            DateTimeOffset.UtcNow.AddDays(-2),
            5,
            "admin",
            DateTimeOffset.UtcNow.AddHours(-1));
        await File.WriteAllTextAsync(
            Path.Combine(dataDir, "artifacts.json"),
            JsonSerializer.Serialize(new[] { expired }, JsonOptions));
        using var store = new ArtifactStore(TestOptions(artifactDir, dataDir));

        await store.ApplyRetentionAsync(CancellationToken.None);

        Assert.False(File.Exists(Path.Combine(artifactDir, fileName)));
        Assert.Empty(await store.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task JobArtifactIsHashedManifestedAndStoredUnderServerOwnedPath()
    {
        string root = Path.Combine(Path.GetTempPath(), $"tracebag-artifact-job-{Guid.NewGuid():N}");
        string artifactDir = Path.Combine(root, "artifacts");
        string dataDir = Path.Combine(root, "data");
        Directory.CreateDirectory(artifactDir);
        Directory.CreateDirectory(dataDir);
        byte[] bytes = new byte[16 * 1024 * 1024];
        Random.Shared.NextBytes(bytes);
        await File.WriteAllBytesAsync(Path.Combine(artifactDir, "job.capture"), bytes);
        DbContextOptions<TracebagDbContext> dbOptions = new DbContextOptionsBuilder<TracebagDbContext>().UseInMemoryDatabase($"artifacts-{Guid.NewGuid():N}").Options;
        using var store = new ArtifactStore(TestOptions(artifactDir, dataDir), new TestFactory(dbOptions));

        ArtifactMetadata artifact = await store.RegisterJobArtifactAsync(
            "artifact-1", "job-1", "target", "api", "cpu-trace", "job.capture", "nettrace", "admin",
            10, 8, "diag:8", "8.0", new { processId = 10 }, new { runnerExitCode = 0 }, CancellationToken.None);
        (ArtifactMetadata Metadata, string Path) download = await store.GetForDownloadAsync("artifact-1", CancellationToken.None);
        (ArtifactMetadata Metadata, string Path) manifest = await store.GetManifestAsync("artifact-1", CancellationToken.None);

        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), artifact.Sha256);
        Assert.Contains("artifact-1/payload.nettrace", artifact.FileName, StringComparison.Ordinal);
        Assert.Equal(bytes.Length, new FileInfo(download.Path).Length);
        Assert.Contains("\"schemaVersion\": 1", await File.ReadAllTextAsync(manifest.Path), StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(artifactDir, "job.capture")));
    }

    [Fact]
    public async Task RejectsTraversalAndReconcilesMissingAndUnknownFiles()
    {
        string root = Path.Combine(Path.GetTempPath(), $"tracebag-artifact-reconcile-{Guid.NewGuid():N}");
        string artifactDir = Path.Combine(root, "artifacts");
        string dataDir = Path.Combine(root, "data");
        Directory.CreateDirectory(artifactDir);
        Directory.CreateDirectory(dataDir);
        DbContextOptions<TracebagDbContext> dbOptions = new DbContextOptionsBuilder<TracebagDbContext>().UseInMemoryDatabase($"artifacts-{Guid.NewGuid():N}").Options;
        var factory = new TestFactory(dbOptions);
        await using (TracebagDbContext db = factory.CreateDbContext())
        {
            db.Artifacts.Add(new ArtifactRecord
            {
                Id = "missing",
                ContainerId = "target",
                ContainerName = "api",
                Type = "cpu-trace",
                FileName = "missing.nettrace",
                CreatedAt = DateTimeOffset.UtcNow,
                Size = 1,
                CreatedBy = "admin",
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
            });
            await db.SaveChangesAsync();
        }
        await File.WriteAllTextAsync(Path.Combine(artifactDir, "unknown.bin"), "unknown");
        using var store = new ArtifactStore(TestOptions(artifactDir, dataDir), factory);

        Assert.Equal("artifact_filename_invalid", Assert.Throws<Tracebag.Api.Security.TracebagException>(() => store.GetArtifactPath("../outside")).Code);
        ArtifactReconciliationResult result = await store.ReconcileAsync(CancellationToken.None);

        Assert.Equal(1, result.MissingFiles);
        Assert.Equal(1, result.QuarantinedFiles);
        Assert.False(File.Exists(Path.Combine(artifactDir, "unknown.bin")));
        Assert.Single(Directory.EnumerateFiles(Path.Combine(artifactDir, "quarantine"), "*", SearchOption.AllDirectories));
        Assert.Equal("missing", (await store.ListAsync(CancellationToken.None)).Single().State);
    }

    [Fact]
    public async Task IncidentEvidencePreventsIndependentArtifactDeletion()
    {
        string root = Path.Combine(Path.GetTempPath(), $"tracebag-artifact-incident-{Guid.NewGuid():N}");
        string artifactDir = Path.Combine(root, "artifacts");
        string dataDir = Path.Combine(root, "data");
        Directory.CreateDirectory(artifactDir);
        Directory.CreateDirectory(dataDir);
        await File.WriteAllTextAsync(Path.Combine(artifactDir, "evidence.txt"), "stacks");
        DbContextOptions<TracebagDbContext> dbOptions = new DbContextOptionsBuilder<TracebagDbContext>().UseInMemoryDatabase($"artifact-incident-{Guid.NewGuid():N}").Options;
        var factory = new TestFactory(dbOptions);
        await using (TracebagDbContext db = factory.CreateDbContext())
        {
            db.Artifacts.Add(new ArtifactRecord { Id = "artifact-1", ContainerId = "target", ContainerName = "api", Type = "stack-snapshot", FileName = "evidence.txt", CreatedAt = DateTimeOffset.UtcNow, Size = 6, CreatedBy = "admin", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) });
            db.Incidents.Add(new IncidentRecord { Id = "incident-1", ContainerId = "target", ContainerName = "api", DockerId = "docker", ProcessId = 1, Title = "Frozen", Profile = "frozen-api", Status = "ready", CreatedBy = "admin", CreatedAt = DateTimeOffset.UtcNow, WindowStart = DateTimeOffset.UtcNow, CaptureOptionsJson = "{}" });
            db.IncidentEvidence.Add(new IncidentEvidenceRecord { Id = "evidence-1", IncidentId = "incident-1", Kind = "diagnostic-artifact", Title = "Stacks", CapturedAt = DateTimeOffset.UtcNow, ArtifactId = "artifact-1", SummaryJson = "{}", PayloadJson = "{}", RedactionStatus = "not-redacted" });
            await db.SaveChangesAsync();
        }
        using var store = new ArtifactStore(TestOptions(artifactDir, dataDir), factory);

        TracebagException exception = await Assert.ThrowsAsync<Tracebag.Api.Security.TracebagException>(() => store.DeleteAsync("artifact-1", CancellationToken.None));

        Assert.Equal("artifact_referenced_by_incident", exception.Code);
        Assert.True(File.Exists(Path.Combine(artifactDir, "evidence.txt")));
    }

    private static TracebagOptions TestOptions(string artifactDir, string dataDir)
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
            ArtifactDir = artifactDir,
            DataDir = dataDir,
            ArtifactVolume = "artifacts",
            DiagnosticImage = "diag",
            PublicBaseUrl = "http://localhost:9090",
            RestartEnabled = false,
            CounterMaxSeconds = 600,
            ArtifactRetentionHours = 24,
            ArtifactMaxCount = 20,
            ArtifactMaxTotalBytes = 1024 * 1024
        };
    }

    private sealed class TestFactory(DbContextOptions<TracebagDbContext> options) : IDbContextFactory<TracebagDbContext>
    {
        public TracebagDbContext CreateDbContext() => new(options);
        public Task<TracebagDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }
}
