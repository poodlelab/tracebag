using Docker.DotNet.Models;
using Tracebag.Api.Auth;
using Tracebag.Api.Diagnostics;
using Tracebag.Api.Models;
using Tracebag.Api.Security;

namespace Tracebag.UnitTests;

public sealed class DiagnosticJobProfileCatalogTests
{
    [Fact]
    public void ExposesOnlyFixedServerOwnedProfiles()
    {
        var catalog = new DiagnosticJobProfileCatalog(Options());

        var profiles = catalog.List();

        Assert.Equal(6, profiles.Count);
        Assert.Contains(profiles, profile => profile.Id == "stack-snapshot");
        Assert.Contains(profiles, profile => profile.Id == "cpu-trace");
        Assert.Contains(profiles, profile => profile.Id == "threading-trace");
        Assert.Contains(profiles, profile => profile.Id == "contention-trace");
        Assert.Contains(profiles, profile => profile.Id == "gc-dump");
        Assert.Contains(profiles, profile => profile.Id == "full-dump" && !profile.Enabled);
    }

    [Fact]
    public void RejectsArbitraryProfileInsteadOfPassingItToRunner()
    {
        var catalog = new DiagnosticJobProfileCatalog(Options());
        var request = new DiagnosticJobCreateRequest(10, "cpu-trace; sh", 30, null);

        var exception = Assert.Throws<TracebagException>(() => catalog.Resolve(request, Target(), "server-owned.capture"));

        Assert.Equal("diagnostic_profile_invalid", exception.Code);
    }

    [Theory]
    [InlineData("gcdump")]
    [InlineData("nettrace")]
    [InlineData("dmp")]
    public void DurableStagingFileKeepsTheToolExtensionLast(string extension)
    {
        var fileName = DiagnosticJobService.BuildStagingFileName("job-123", extension);

        Assert.Equal($"job-123.capture.{extension}", fileName);
        Assert.EndsWith($".{extension}", fileName, StringComparison.Ordinal);
    }

    [Fact]
    public void FullDumpRequiresGlobalTargetAndConfirmationOptIn()
    {
        var request = new DiagnosticJobCreateRequest(10, "full-dump", null, DiagnosticJobProfileCatalog.FullDumpConfirmation);
        var globallyDisabled = new DiagnosticJobProfileCatalog(Options());
        Assert.Equal("full_dump_globally_disabled", Assert.Throws<TracebagException>(() => globallyDisabled.Resolve(request, Target(), "job.capture")).Code);

        var enabled = new DiagnosticJobProfileCatalog(Options() with { FullDumpEnabled = true });
        Assert.Equal("full_dump_target_disabled", Assert.Throws<TracebagException>(() => enabled.Resolve(request, Target(), "job.capture")).Code);

        var target = Target();
        target.Labels["tracebag.diagnostics.fullDump"] = "true";
        Assert.Equal("full_dump_confirmation_required", Assert.Throws<TracebagException>(() => enabled.Resolve(request with { Confirmation = null }, target, "job.capture")).Code);
        var resolved = enabled.Resolve(request, target, "job.capture");
        Assert.Equal(["dump-full", "10", "job.capture"], resolved.Command);
    }

    private static ContainerListResponse Target() => new()
    {
        ID = "target",
        Names = ["/api"],
        Labels = new Dictionary<string, string>()
    };

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
        ArtifactMaxTotalBytes = 1024 * 1024
    };
}
