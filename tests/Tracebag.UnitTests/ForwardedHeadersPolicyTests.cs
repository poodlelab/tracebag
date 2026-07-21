using Microsoft.Extensions.Configuration;
using Tracebag.Api.Auth;
using Tracebag.Api.Security;

namespace Tracebag.UnitTests;

public sealed class ForwardedHeadersPolicyTests
{
    [Fact]
    public void TrustsOnlyLoopbackByDefault()
    {
        var options = ForwardedHeadersPolicy.Create(Options(null));

        Assert.Contains(System.Net.IPAddress.Loopback, options.KnownProxies);
        Assert.Contains(System.Net.IPAddress.IPv6Loopback, options.KnownProxies);
        Assert.Empty(options.KnownNetworks);
        Assert.Equal(1, options.ForwardLimit);
        Assert.True(options.RequireHeaderSymmetry);
    }

    [Fact]
    public void ParsesExplicitIpAndCidrEntries()
    {
        var options = ForwardedHeadersPolicy.Create(Options("10.0.0.5,172.20.0.0/16,fd00::/64"));

        Assert.Contains(System.Net.IPAddress.Parse("10.0.0.5"), options.KnownProxies);
        Assert.Equal(2, options.KnownNetworks.Count);
    }

    [Theory]
    [InlineData("not-an-address")]
    [InlineData("10.0.0.0/99")]
    [InlineData("fd00::/999")]
    public void RejectsInvalidEntries(string value)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => ForwardedHeadersPolicy.Create(Options(value)));

        Assert.Contains("TRACEBAG_TRUSTED_PROXIES", exception.Message, StringComparison.Ordinal);
    }

    private static TracebagOptions Options(string? trustedProxies)
    {
        return TracebagOptions.FromConfiguration(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TRACEBAG_TRUSTED_PROXIES"] = trustedProxies
            })
            .Build());
    }
}
