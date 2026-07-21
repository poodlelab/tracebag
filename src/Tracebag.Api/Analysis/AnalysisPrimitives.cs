using System.Diagnostics;
using System.Text.RegularExpressions;
using Tracebag.Api.Models;

namespace Tracebag.Api.Analysis;

public sealed record AnalyzerOutput(
    AnalysisComponent Component,
    IReadOnlyList<AnalysisObservation> Observations,
    IReadOnlyList<AnalysisLimitation> Limitations);

public static partial class StackFrameNormalizer
{
    [GeneratedRegex(@"^\s*(?:at\s+)?", RegexOptions.IgnoreCase)]
    private static partial Regex AtPrefix();

    [GeneratedRegex(@"\s*(?:\+0x[0-9a-f]+|\[0x[0-9a-f]+\]|\s+in\s+.+?:line\s+\d+)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex VolatileSuffix();

    [GeneratedRegex(@"(?<type>.+)\+<(?<method>[^>]+)>d__\d+\.MoveNext\(\)")]
    private static partial Regex AsyncStateMachine();

    public static string Normalize(string frame)
    {
        string value = AtPrefix().Replace(frame.Trim(), string.Empty);
        value = VolatileSuffix().Replace(value, string.Empty);
        value = Regex.Replace(value, @"\([^)]*\)$", "()");
        var asyncMatch = AsyncStateMachine().Match(value);
        if (asyncMatch.Success)
        {
            value = $"{asyncMatch.Groups["type"].Value}.{asyncMatch.Groups["method"].Value}() [async]";
        }
        return value.Length > 500 ? value[..500] : value;
    }

    public static bool IsUseful(string value) =>
        value.Length > 2 &&
        value[0] != '[' &&
        !value.StartsWith("Thread ", StringComparison.OrdinalIgnoreCase) &&
        !value.StartsWith("OS Thread", StringComparison.OrdinalIgnoreCase);
}

public static class AnalyzerTiming
{
    public static AnalysisComponent Component(string name, string status, Stopwatch stopwatch, int count, string? error = null) =>
        new(name, status, stopwatch.ElapsedMilliseconds, count, error);
}
