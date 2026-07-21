using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace Tracebag.Api.Security;

public static class ForwardedHeadersPolicy
{
    public static ForwardedHeadersOptions Create(Tracebag.Api.Auth.TracebagOptions tracebagOptions)
    {
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            ForwardLimit = 1,
            RequireHeaderSymmetry = true
        };
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
        options.KnownProxies.Add(IPAddress.Loopback);
        options.KnownProxies.Add(IPAddress.IPv6Loopback);

        foreach (var entry in tracebagOptions.TrustedProxies)
        {
            Add(options, entry);
        }

        return options;
    }

    private static void Add(ForwardedHeadersOptions options, string entry)
    {
        var separator = entry.LastIndexOf('/');
        if (separator < 0)
        {
            if (!IPAddress.TryParse(entry, out var proxy))
            {
                throw Invalid(entry);
            }

            options.KnownProxies.Add(proxy);
            return;
        }

        var addressText = entry[..separator];
        var prefixText = entry[(separator + 1)..];
        if (!IPAddress.TryParse(addressText, out var address)
            || !int.TryParse(prefixText, out var prefixLength)
            || prefixLength < 0
            || prefixLength > (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128))
        {
            throw Invalid(entry);
        }

        options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(address, prefixLength));
    }

    private static InvalidOperationException Invalid(string entry)
    {
        return new InvalidOperationException(
            $"Invalid TRACEBAG_TRUSTED_PROXIES entry '{entry}'. Use a literal IP address or CIDR network.");
    }
}
