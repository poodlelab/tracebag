namespace Tracebag.Api.Logs;

public static class LogTimestampNormalizer
{
    public static NormalizedLogLine Normalize(string line, DateTimeOffset receivedAt)
    {
        var separator = line.IndexOf(' ');
        if (separator > 0)
        {
            var token = line[..separator];
            if (DateTimeOffset.TryParse(token, out var timestamp))
            {
                return new NormalizedLogLine(timestamp.ToUniversalTime(), token, line[(separator + 1)..]);
            }
        }

        return new NormalizedLogLine(receivedAt, string.Empty, line);
    }
}

public sealed record NormalizedLogLine(DateTimeOffset Timestamp, string SourceTimestamp, string Line);
