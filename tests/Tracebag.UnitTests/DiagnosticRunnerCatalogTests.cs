using Docker.DotNet.Models;
using Tracebag.Api.Auth;
using Tracebag.Api.Diagnostics;
using Tracebag.Api.Security;

namespace Tracebag.UnitTests;

public sealed class DiagnosticRunnerCatalogTests
{
    [Theory]
    [InlineData("8", 8, "diag:8")]
    [InlineData("9.0", 9, "diag:9")]
    [InlineData("10", 10, "diag:10")]
    public void SelectsRunnerFromTargetRuntime(string label, int major, string image)
    {
        var catalog = new DiagnosticRunnerCatalog(Options());
        var container = new ContainerListResponse
        {
            Labels = new Dictionary<string, string> { [DiagnosticRunnerCatalog.RuntimeLabel] = label }
        };

        var selected = catalog.Select(container);

        Assert.Equal(major, selected.RuntimeMajor);
        Assert.Equal(image, selected.Image);
        Assert.True(selected.RuntimeWasExplicit);
    }

    [Fact]
    public void FallsBackToConfiguredRuntimeWhenLabelIsMissing()
    {
        var selected = new DiagnosticRunnerCatalog(Options()).Select(new ContainerListResponse());

        Assert.Equal(8, selected.RuntimeMajor);
        Assert.False(selected.RuntimeWasExplicit);
    }

    [Fact]
    public void RejectsUnsupportedRuntimeLabel()
    {
        var container = new ContainerListResponse
        {
            Labels = new Dictionary<string, string> { [DiagnosticRunnerCatalog.RuntimeLabel] = "7" }
        };

        var error = Assert.Throws<TracebagException>(() => new DiagnosticRunnerCatalog(Options()).Select(container));

        Assert.Equal("dotnet_runtime_unsupported", error.Code);
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
        DiagnosticImage = "diag:8",
        DiagnosticImageDotnet9 = "diag:9",
        DiagnosticImageDotnet10 = "diag:10",
        PublicBaseUrl = "http://localhost",
        RestartEnabled = false,
        CounterMaxSeconds = 600,
        ArtifactRetentionHours = 24,
        ArtifactMaxCount = 20,
        ArtifactMaxTotalBytes = 1024 * 1024
    };
}
