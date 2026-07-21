using System.Globalization;

namespace Tracebag.Api.Diagnostics;

public sealed class CounterSampleParser
{
    public bool TryParseMetric(string line, DateTimeOffset receivedAt, out CounterMetric metric)
    {
        metric = default;
        if (!TryParse(line, out var sample))
        {
            return false;
        }

        metric = new CounterMetric(
            $"{sample.Provider}|{sample.Name}|{sample.CounterType}",
            sample.CapturedAt,
            sample.Provider,
            sample.Name,
            sample.CounterType,
            sample.Value,
            sample.Value.ToString("R", CultureInfo.InvariantCulture),
            receivedAt.ToUniversalTime());
        return true;
    }

    public bool TryParse(string line, out CounterSample sample)
    {
        sample = default;
        line = line.Trim();
        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("Timestamp,", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var columns = ParseCsvLine(line);
        if (columns.Count < 5)
        {
            return false;
        }

        var timestampText = columns[0];
        var provider = columns[1];
        var name = columns[2];
        var counterType = columns[3];
        var valueText = columns[4];
        if (string.IsNullOrWhiteSpace(provider)
            || string.IsNullOrWhiteSpace(name)
            || string.IsNullOrWhiteSpace(counterType)
            || string.IsNullOrWhiteSpace(valueText))
        {
            return false;
        }

        if (!double.TryParse(valueText, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            && !double.TryParse(valueText, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
        {
            return false;
        }

        var capturedAt = ParseTimestamp(timestampText) ?? DateTimeOffset.UtcNow;
        sample = new CounterSample(capturedAt, provider, name, counterType, value);
        return true;
    }

    private static DateTimeOffset? ParseTimestamp(string value)
    {
        if (DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var timestamp))
        {
            return timestamp;
        }

        return DateTimeOffset.TryParse(value, out timestamp) ? timestamp.ToUniversalTime() : null;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var values = new List<string>();
        var value = string.Empty;
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    value += '"';
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }

                continue;
            }

            if (ch == ',' && !inQuotes)
            {
                values.Add(value.Trim());
                value = string.Empty;
                continue;
            }

            value += ch;
        }

        values.Add(value.Trim());
        return values;
    }
}

public readonly record struct CounterSample(
    DateTimeOffset CapturedAt,
    string Provider,
    string Name,
    string CounterType,
    double Value);

public readonly record struct CounterMetric(
    string Id,
    DateTimeOffset Timestamp,
    string Provider,
    string Name,
    string CounterType,
    double Value,
    string ValueText,
    DateTimeOffset ReceivedAt);
