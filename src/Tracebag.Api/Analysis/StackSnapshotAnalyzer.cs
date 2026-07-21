using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Tracebag.Api.Auth;
using Tracebag.Api.Models;

namespace Tracebag.Api.Analysis;

public sealed class StackSnapshotAnalyzer(TracebagOptions options)
{
    public async Task<AnalyzerOutput> AnalyzeAsync(string path, IncidentEvidenceDto evidence, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var file = new FileInfo(path);
        if (file.Length > options.AnalysisMaxStackBytes)
        {
            throw new InvalidDataException($"Stack snapshot is larger than the configured {options.AnalysisMaxStackBytes} byte analysis limit.");
        }

        string text = await File.ReadAllTextAsync(path, cancellationToken);
        var groups = Group(text);
        var observations = new List<AnalysisObservation>();
        if (groups.Count > 0)
        {
            var top = groups.Take(20).Select(x => new { x.Signature, x.Count, threadIds = x.ThreadIds.Take(20), frames = x.Frames.Take(40) }).ToArray();
            observations.Add(new AnalysisObservation(
                $"obs-{Guid.NewGuid():N}", "stack", "grouped-stacks", "info", "high",
                $"{groups.Sum(x => x.Count)} managed thread stacks grouped into {groups.Count} shapes",
                "Repeated stack shapes are normalized so volatile addresses and async state-machine names do not split equivalent stacks.",
                [evidence.Id], new { groups = top }));

            var blocked = groups.Where(x => x.Frames.Any(IsBlockingFrame)).ToArray();
            if (blocked.Length > 0)
            {
                int blockedCount = blocked.Sum(x => x.Count);
                observations.Add(new AnalysisObservation(
                    $"obs-{Guid.NewGuid():N}", "stack", "blocked-thread-stacks", "warning", blockedCount >= 3 ? "high" : "medium",
                    $"{blockedCount} captured threads share blocking or synchronization frames",
                    "Inspect the grouped call paths before treating this as starvation: a single stack snapshot cannot prove how long a thread remained blocked.",
                    [evidence.Id], new { groups = blocked.Take(10).Select(x => new { x.Count, frames = x.Frames.Take(20) }) }));
            }
        }

        var limitations = groups.Count == 0
            ? new[] { new AnalysisLimitation("stack-format-unrecognized", "The snapshot contained no recognizable managed thread stacks.", evidence.Id) }
            : [new AnalysisLimitation("stack-snapshot-instantaneous", "Stack snapshots show one instant and do not establish duration or CPU cost by themselves.", evidence.Id)];
        return new AnalyzerOutput(AnalyzerTiming.Component("stack", groups.Count == 0 ? "partial" : "completed", stopwatch, observations.Count), observations, limitations);
    }

    public static IReadOnlyList<StackGroup> Group(string text)
    {
        var stacks = new List<(string ThreadId, List<string> Frames)>();
        string threadId = "unknown";
        var frames = new List<string>();
        void Flush()
        {
            if (frames.Count > 0)
            {
                stacks.Add((threadId, [.. frames]));
            }

            frames.Clear();
        }

        foreach (string? raw in text.Split('\n').Take(100_000))
        {
            string line = raw.TrimEnd('\r');
            if (line.TrimStart().StartsWith("Thread ", StringComparison.OrdinalIgnoreCase) || line.TrimStart().StartsWith("OS Thread", StringComparison.OrdinalIgnoreCase))
            {
                Flush();
                threadId = line.Trim().Split(':', 2)[0];
                continue;
            }
            string normalized = StackFrameNormalizer.Normalize(line);
            if (StackFrameNormalizer.IsUseful(normalized) && (char.IsWhiteSpace(line.FirstOrDefault()) || normalized.Contains('.', StringComparison.Ordinal)))
            {
                frames.Add(normalized);
            }
        }
        Flush();

        return [.. stacks.GroupBy(x => string.Join('\n', x.Frames), StringComparer.Ordinal)
            .Select(group => new StackGroup(
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(group.Key)))[..12].ToLowerInvariant(),
                group.Count(), [.. group.Select(x => x.ThreadId).Distinct()], group.First().Frames))
            .OrderByDescending(x => x.Count).ThenBy(x => x.Signature, StringComparer.Ordinal)];
    }

    private static bool IsBlockingFrame(string frame) =>
        frame.Contains("Monitor.Enter", StringComparison.Ordinal) || frame.Contains("SemaphoreSlim.Wait", StringComparison.Ordinal) ||
        frame.Contains("Task.Wait", StringComparison.Ordinal) || frame.Contains("WaitOne", StringComparison.Ordinal) ||
        frame.Contains("GetResult", StringComparison.Ordinal) || frame.Contains("Thread.Sleep", StringComparison.Ordinal);
}

public sealed record StackGroup(string Signature, int Count, IReadOnlyList<string> ThreadIds, IReadOnlyList<string> Frames);
