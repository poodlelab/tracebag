using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Tracebag.Api.Auth;
using Tracebag.Api.Health;

namespace Tracebag.UnitTests;

public sealed class HealthCheckTests
{
    [Fact]
    public async Task ArtifactProbeConfirmsWritableStorageAndCleansUp()
    {
        var root = Path.Combine(Path.GetTempPath(), $"tracebag-health-tests-{Guid.NewGuid():N}");
        var artifactDirectory = Path.Combine(root, "artifacts");
        var options = Options(artifactDirectory);
        var healthCheck = new ArtifactStorageHealthCheck(options);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Empty(Directory.EnumerateFiles(artifactDirectory));
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task ArtifactProbeReportsUnusableStorageAsUnhealthy()
    {
        var root = Path.Combine(Path.GetTempPath(), $"tracebag-health-tests-{Guid.NewGuid():N}");
        await File.WriteAllTextAsync(root, "not a directory");
        var healthCheck = new ArtifactStorageHealthCheck(Options(root));

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        File.Delete(root);
    }

    [Fact]
    public async Task DatabaseProbeReportsDevelopmentFallbackAsHealthy()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var healthCheck = new DatabaseHealthCheck(services, Options(Path.GetTempPath()));

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("not configured", result.Description, StringComparison.Ordinal);
    }

    private static TracebagOptions Options(string artifactDirectory)
    {
        return TracebagOptions.FromConfiguration(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TRACEBAG_AUTH_ENABLED"] = "false",
                ["TRACEBAG_ARTIFACT_DIR"] = artifactDirectory,
                ["TRACEBAG_DATA_DIR"] = Path.Combine(artifactDirectory, "data")
            })
            .Build());
    }
}
