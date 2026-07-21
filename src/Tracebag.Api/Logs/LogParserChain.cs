using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tracebag.Api.Logs;

public sealed partial class LogParserChain
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public ParsedLogLine Parse(string parser, string line)
    {
        var normalizedParser = NormalizeParser(parser);
        if (normalizedParser is "auto" or "json" or "serilog"
            && TryParseJson(line, normalizedParser == "serilog", out var json))
        {
            return json;
        }

        return ParsePlain(line);
    }

    public static string NormalizeParser(string? parser)
    {
        return parser?.Trim().ToLowerInvariant() switch
        {
            "plain" => "plain",
            "json" => "json",
            "serilog" => "serilog",
            _ => "auto"
        };
    }

    private static bool TryParseJson(string line, bool serilogOnly, out ParsedLogLine parsed)
    {
        parsed = default!;
        try
        {
            using var document = JsonDocument.Parse(line);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var root = document.RootElement;
            if (serilogOnly && !root.EnumerateObject().Any(property => property.Name.StartsWith('@')))
            {
                return false;
            }

            var message = ReadString(root, "@m", "message", "Message", "@mt") ?? line;
            var level = NormalizeLevel(ReadString(root, "@l", "level", "Level", "logLevel", "LogLevel"));
            var exception = ReadString(root, "@x", "exception", "Exception", "exceptionType", "ExceptionType");
            var exceptionType = ReadString(root, "exceptionType", "ExceptionType") ?? ExtractExceptionType(exception);
            var traceId = ReadString(root, "traceId", "TraceId", "trace_id", "ActivityTraceId", "RequestId");
            var timestamp = ReadTimestamp(root, "@t", "timestamp", "Timestamp", "time", "Time");
            var properties = root.EnumerateObject().ToDictionary(
                property => property.Name,
                property => ToValue(property.Value),
                StringComparer.Ordinal);
            parsed = new ParsedLogLine(
                message,
                level,
                exceptionType,
                traceId,
                timestamp,
                JsonSerializer.Serialize(properties, JsonOptions));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static ParsedLogLine ParsePlain(string line)
    {
        var levelMatch = LevelPattern().Match(line);
        var level = levelMatch.Success ? NormalizeLevel(levelMatch.Groups[1].Value) : null;
        return new ParsedLogLine(
            line,
            level,
            ExtractExceptionType(line),
            ExtractTraceId(line),
            null,
            null);
    }

    private static string? ReadString(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.TryGetProperty(name, out var property))
            {
                return property.ValueKind == JsonValueKind.String
                    ? property.GetString()
                    : property.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined
                        ? property.ToString()
                        : null;
            }
        }

        return null;
    }

    private static DateTimeOffset? ReadTimestamp(JsonElement root, params string[] names)
    {
        var value = ReadString(root, names);
        return DateTimeOffset.TryParse(value, out var timestamp) ? timestamp.ToUniversalTime() : null;
    }

    private static object? ToValue(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number when value.TryGetInt64(out var integer) => integer,
            JsonValueKind.Number when value.TryGetDouble(out var number) => number,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => value.GetRawText()
        };
    }

    private static string? NormalizeLevel(string? level)
    {
        return level?.Trim().ToLowerInvariant() switch
        {
            "v" or "verbose" or "trace" or "trc" => "trace",
            "d" or "debug" or "dbg" => "debug",
            "i" or "information" or "info" or "inf" => "information",
            "w" or "warning" or "warn" or "wrn" => "warning",
            "e" or "error" or "err" or "fail" => "error",
            "f" or "fatal" or "critical" or "crit" or "ftl" => "critical",
            null or "" => null,
            var other => other[..Math.Min(other.Length, 40)]
        };
    }

    private static string? ExtractExceptionType(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = ExceptionPattern().Match(text);
        return match.Success ? match.Groups[1].Value[..Math.Min(match.Groups[1].Value.Length, 240)] : null;
    }

    private static string? ExtractTraceId(string text)
    {
        var match = TracePattern().Match(text);
        return match.Success ? match.Groups[1].Value : null;
    }

    [GeneratedRegex(@"(?:^|[\s\[])(trace|trc|debug|dbg|information|info|inf|warning|warn|wrn|error|err|critical|fatal|ftl)(?:[\s\]:-]|$)", RegexOptions.IgnoreCase)]
    private static partial Regex LevelPattern();

    [GeneratedRegex(@"\b([A-Za-z_][A-Za-z0-9_.+`]*Exception)\b")]
    private static partial Regex ExceptionPattern();

    [GeneratedRegex(@"(?:trace(?:id)?)[\s:=]+([A-Za-z0-9_-]{8,128})", RegexOptions.IgnoreCase)]
    private static partial Regex TracePattern();
}

public sealed record ParsedLogLine(
    string Message,
    string? Level,
    string? ExceptionType,
    string? TraceId,
    DateTimeOffset? ApplicationTimestamp,
    string? PropertiesJson);
