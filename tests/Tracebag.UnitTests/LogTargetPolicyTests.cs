using Docker.DotNet.Models;
using Microsoft.Extensions.Configuration;
using Tracebag.Api.Auth;
using Tracebag.Api.Docker;
using Tracebag.Api.Logs;

namespace Tracebag.UnitTests;

public sealed class LogTargetPolicyTests
{
    [Fact]
    public void RequiresExplicitPersistenceOptIn()
    {
        var options = Options();
        var policy = new LogTargetPolicy(options, new ContainerPolicy(options));
        var container = Container(new Dictionary<string, string> { ["tracebag.enabled"] = "true" });

        Assert.Null(policy.Resolve(container));

        container.Labels["tracebag.logs.persist"] = "true";
        Assert.NotNull(policy.Resolve(container));
    }

    [Fact]
    public void NormalizesParserAndCapsTargetRetentionAndStorage()
    {
        var options = Options();
        var policy = new LogTargetPolicy(options, new ContainerPolicy(options));
        var container = Container(new Dictionary<string, string>
        {
            ["tracebag.enabled"] = "true",
            ["tracebag.logs.persist"] = "true",
            ["tracebag.logs.parser"] = "invalid",
            ["tracebag.logs.retentionDays"] = "99",
            ["tracebag.logs.maxBytes"] = "999999999"
        });

        var target = Assert.IsType<LogTarget>(policy.Resolve(container));

        Assert.Equal("auto", target.Parser);
        Assert.Equal(options.LogRetentionDays, target.RetentionDays);
        Assert.Equal(options.LogMaxBytesPerContainer, target.MaxBytes);
    }

    private static ContainerListResponse Container(Dictionary<string, string> labels)
    {
        return new ContainerListResponse
        {
            ID = "docker-1",
            Image = "demo:test",
            Created = DateTime.UtcNow,
            Names = ["/demo-api"],
            Labels = labels
        };
    }

    private static TracebagOptions Options()
    {
        return TracebagOptions.FromConfiguration(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TRACEBAG_AUTH_ENABLED"] = "false",
                ["TRACEBAG_LOG_RETENTION_DAYS"] = "7",
                ["TRACEBAG_LOG_MAX_TOTAL_BYTES"] = "4000000",
                ["TRACEBAG_LOG_MAX_BYTES_PER_CONTAINER"] = "2000000"
            })
            .Build());
    }
}
