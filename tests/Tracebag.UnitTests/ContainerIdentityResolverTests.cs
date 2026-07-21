using Docker.DotNet.Models;
using Tracebag.Api.Auth;
using Tracebag.Api.Docker;

namespace Tracebag.UnitTests;

public sealed class ContainerIdentityResolverTests
{
    private readonly ContainerIdentityResolver _resolver = new();

    [Fact]
    public void ComposeRecreationKeepsLogicalIdentity()
    {
        var first = ComposeContainer("docker-first");
        var replacement = ComposeContainer("docker-replacement");

        var firstIdentity = _resolver.Resolve(first);
        var replacementIdentity = _resolver.Resolve(replacement);

        Assert.Equal("compose:orders:api:1", firstIdentity.Id);
        Assert.Equal(firstIdentity.Id, replacementIdentity.Id);
        Assert.NotEqual(first.ID, replacement.ID);
    }

    [Fact]
    public void ExplicitIdentityTakesPrecedenceOverComposeLabels()
    {
        var container = ComposeContainer("docker-first");
        container.Labels["tracebag.identity"] = "Payments Primary";

        var identity = _resolver.Resolve(container);

        Assert.Equal("custom:payments-primary", identity.Id);
        Assert.Equal("explicit-label", identity.Source);
    }

    [Fact]
    public void InternalEventCannotBecomeTargetEvenWhenEnabled()
    {
        var policy = new ContainerPolicy(TestOptions(), _resolver);
        var labels = new Dictionary<string, string>
        {
            ["tracebag.enabled"] = "true",
            ["tracebag.internal"] = "true"
        };

        Assert.False(policy.IsAllowedEvent("docker-id", "internal", labels));
    }

    private static ContainerListResponse ComposeContainer(string dockerId)
    {
        return new ContainerListResponse
        {
            ID = dockerId,
            Image = "orders:test",
            State = "running",
            Status = "Up",
            Created = DateTime.UtcNow,
            Names = ["/orders-api-1"],
            Labels = new Dictionary<string, string>
            {
                ["tracebag.enabled"] = "true",
                ["tracebag.kind"] = "dotnet",
                ["com.docker.compose.project"] = "orders",
                ["com.docker.compose.service"] = "api",
                ["com.docker.compose.container-number"] = "1"
            }
        };
    }

    internal static TracebagOptions TestOptions()
    {
        return new TracebagOptions
        {
            Stage = "test",
            AuthEnabled = true,
            AdminUser = "admin",
            AdminPasswordHash = "hash",
            AllowedLabelKey = "tracebag.enabled",
            AllowedLabelValue = "true",
            ArtifactDir = "/tmp/tracebag-tests/artifacts",
            DataDir = "/tmp/tracebag-tests/data",
            ArtifactVolume = "artifacts",
            DiagnosticImage = "runner:test",
            PublicBaseUrl = "http://localhost:9090",
            RestartEnabled = false,
            CounterMaxSeconds = 600,
            ArtifactRetentionHours = 24,
            ArtifactMaxCount = 20,
            ArtifactMaxTotalBytes = 1024 * 1024
        };
    }
}
