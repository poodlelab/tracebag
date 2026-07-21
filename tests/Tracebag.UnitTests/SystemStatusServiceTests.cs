using Microsoft.Extensions.Configuration;
using Tracebag.Api.Auth;
using Tracebag.Api.Docker;

namespace Tracebag.UnitTests;

public sealed class SystemStatusServiceTests
{
    [Fact]
    public void DiscoveryScopeNeedsAttentionWithoutEnvironmentLabel()
    {
        var options = Options(null);

        var status = SystemStatusService.CreateDiscoveryScopeStatus(options);

        Assert.Equal("attention", status.Status);
        Assert.Null(status.Details["environmentLabel"]);
    }

    [Fact]
    public void DiscoveryScopeIsHealthyWithEnvironmentLabel()
    {
        var options = Options("tracebag.environment=production");

        var status = SystemStatusService.CreateDiscoveryScopeStatus(options);

        Assert.Equal("healthy", status.Status);
        Assert.Equal("tracebag.environment=production", status.Details["environmentLabel"]);
    }

    private static TracebagOptions Options(string? environmentLabel)
    {
        var values = new Dictionary<string, string?>
        {
            ["TRACEBAG_STAGE"] = "local",
            ["TRACEBAG_AUTH_ENABLED"] = "false",
            ["TRACEBAG_ENVIRONMENT_LABEL"] = environmentLabel
        };
        return TracebagOptions.FromConfiguration(
            new ConfigurationBuilder().AddInMemoryCollection(values).Build());
    }
}
