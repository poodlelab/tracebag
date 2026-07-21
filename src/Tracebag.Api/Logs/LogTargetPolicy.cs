using Docker.DotNet.Models;
using Tracebag.Api.Auth;
using Tracebag.Api.Docker;

namespace Tracebag.Api.Logs;

public sealed class LogTargetPolicy(
    TracebagOptions options,
    ContainerPolicy containerPolicy)
{
    public LogTarget? Resolve(ContainerListResponse container)
    {
        var labels = container.Labels ?? new Dictionary<string, string>();
        if (!options.LogIngestionEnabled
            || !labels.TryGetValue("tracebag.logs.persist", out var persist)
            || !bool.TryParse(persist, out var enabled)
            || !enabled)
        {
            return null;
        }

        var identity = containerPolicy.GetIdentity(container);
        var parser = labels.TryGetValue("tracebag.logs.parser", out var parserLabel)
            ? LogParserChain.NormalizeParser(parserLabel)
            : "auto";
        var retentionDays = ReadInt(labels, "tracebag.logs.retentionDays", options.LogRetentionDays, 1, options.LogRetentionDays);
        var maxBytes = ReadLong(
            labels,
            "tracebag.logs.maxBytes",
            options.LogMaxBytesPerContainer,
            1_048_576,
            Math.Min(options.LogMaxBytesPerContainer, options.LogMaxTotalBytes));
        return new LogTarget(
            identity.Id,
            container.ID,
            containerPolicy.GetContainerName(container),
            container.Image,
            parser,
            retentionDays,
            maxBytes,
            new DateTimeOffset(DateTime.SpecifyKind(container.Created, DateTimeKind.Utc)));
    }

    private static int ReadInt(
        IDictionary<string, string> labels,
        string key,
        int fallback,
        int minimum,
        int maximum)
    {
        return labels.TryGetValue(key, out var raw) && int.TryParse(raw, out var value)
            ? Math.Clamp(value, minimum, maximum)
            : fallback;
    }

    private static long ReadLong(
        IDictionary<string, string> labels,
        string key,
        long fallback,
        long minimum,
        long maximum)
    {
        return labels.TryGetValue(key, out var raw) && long.TryParse(raw, out var value)
            ? Math.Clamp(value, minimum, maximum)
            : fallback;
    }
}
