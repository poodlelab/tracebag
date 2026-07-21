using Docker.DotNet.Models;
using Tracebag.Api.Auth;
using Tracebag.Api.Docker;

namespace Tracebag.UnitTests;

public sealed class ContainerPolicyTests
{
    [Fact]
    public void AllowsEnabledContainerWithoutEnvironmentFilter()
    {
        var policy = new ContainerPolicy(TestOptions());
        var container = ContainerWithLabels(new Dictionary<string, string>
        {
            ["tracebag.enabled"] = "true",
            ["tracebag.kind"] = "dotnet"
        });

        Assert.True(policy.IsAllowed(container));
    }

    [Fact]
    public void RejectsContainerThatDoesNotMatchConfiguredEnvironment()
    {
        var policy = new ContainerPolicy(TestOptions("tracebag.environment=prod"));
        var container = ContainerWithLabels(new Dictionary<string, string>
        {
            ["tracebag.enabled"] = "true",
            ["tracebag.environment"] = "test",
            ["tracebag.kind"] = "dotnet"
        });

        Assert.False(policy.IsAllowed(container));
    }

    [Fact]
    public void RejectsTracebagRunnerContainers()
    {
        var policy = new ContainerPolicy(TestOptions());
        var container = ContainerWithLabels(new Dictionary<string, string>
        {
            ["tracebag.enabled"] = "true",
            ["tracebag.runner"] = "true"
        });

        Assert.False(policy.IsAllowed(container));
    }

    [Fact]
    public void AllowsOptedInDemoContainerWithTracebagInItsName()
    {
        var policy = new ContainerPolicy(TestOptions());
        var container = ContainerWithLabels(new Dictionary<string, string>
        {
            ["tracebag.enabled"] = "true",
            ["tracebag.kind"] = "dotnet"
        });
        container.Names = ["/tracebag-demo-api"];

        Assert.True(policy.IsAllowed(container));
    }

    private static ContainerListResponse ContainerWithLabels(Dictionary<string, string> labels)
    {
        return new ContainerListResponse
        {
            ID = "abcdef123456",
            Image = "tracebag-api:test",
            Status = "Up",
            State = "running",
            Created = DateTime.UtcNow,
            Names = ["/tracebag_test-api-1"],
            Labels = labels
        };
    }

    private static TracebagOptions TestOptions(string? environmentLabel = null)
    {
        var environmentParts = environmentLabel?.Split('=', 2);
        return new TracebagOptions
        {
            Stage = "test",
            AuthEnabled = true,
            AdminUser = "admin",
            AdminPasswordHash = "hash",
            AllowedLabelKey = "tracebag.enabled",
            AllowedLabelValue = "true",
            EnvironmentLabelKey = environmentParts?[0],
            EnvironmentLabelValue = environmentParts?[1],
            ArtifactDir = "/tmp/artifacts",
            DataDir = "/tmp/data",
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
}
