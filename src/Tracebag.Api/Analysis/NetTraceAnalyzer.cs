using System.Diagnostics;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Tracebag.Api.Auth;
using Tracebag.Api.Models;

namespace Tracebag.Api.Analysis;

public sealed class NetTraceAnalyzer(TracebagOptions options)
{
    public Task<AnalyzerOutput> AnalyzeAsync(string path, IncidentEvidenceDto evidence, CancellationToken cancellationToken) =>
        Task.Run(() => Analyze(path, evidence, cancellationToken), cancellationToken);

    private AnalyzerOutput Analyze(string path, IncidentEvidenceDto evidence, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var file = new FileInfo(path);
        if (file.Length > options.AnalysisMaxTraceBytes)
        {
            throw new InvalidDataException($"Trace is larger than the configured {options.AnalysisMaxTraceBytes} byte analysis limit.");
        }

        var accumulator = new TraceAccumulator(options.AnalysisMaxEvents);
        string etlxPath = Path.Combine(Path.GetTempPath(), $"tracebag-analysis-{Guid.NewGuid():N}.etlx");
        try
        {
            TraceLog.CreateFromEventPipeDataFile(path, etlxPath, new TraceLogOptions());
            using var log = new TraceLog(etlxPath);
            foreach (TraceEvent data in log.Events)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (accumulator.EventCount >= options.AnalysisMaxEvents)
                {
                    break;
                }
                accumulator.Accept(data, ReadStack(data));
            }
        }
        finally
        {
            if (File.Exists(etlxPath))
            {
                File.Delete(etlxPath);
            }
        }

        IReadOnlyList<AnalysisObservation> observations = accumulator.Build(evidence.Id);
        var limitations = new List<AnalysisLimitation>
        {
            new("trace-symbols-best-effort", "Hot paths use symbols embedded or resolvable in the local trace; unresolved native frames may remain addresses.", evidence.Id)
        };
        if (accumulator.EventCount >= options.AnalysisMaxEvents)
        {
            limitations.Add(new("trace-event-limit", $"Analysis stopped after the configured {options.AnalysisMaxEvents} event limit.", evidence.Id));
        }
        if (accumulator.SampleCount == 0)
        {
            limitations.Add(new("cpu-samples-unavailable", "This trace contained no recognized sample-profiler events, so CPU hot paths could not be calculated.", evidence.Id));
        }
        return new AnalyzerOutput(AnalyzerTiming.Component("trace", "completed", stopwatch, observations.Count), observations, limitations);
    }

    private static List<string> ReadStack(TraceEvent data)
    {
        var result = new List<string>();
        try
        {
            for (TraceCallStack? frame = data.CallStack(); frame is not null && result.Count < 128; frame = frame.Caller)
            {
                string name = StackFrameNormalizer.Normalize(frame.CodeAddress.FullMethodName);
                if (StackFrameNormalizer.IsUseful(name))
                {
                    result.Add(name);
                }
            }
        }
        catch { }
        return result;
    }
}

public sealed class TraceAccumulator(int maxEvents)
{
    private readonly Dictionary<string, int> _frames = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _exceptions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _threadPool = new(StringComparer.Ordinal);
    private readonly List<double> _gcPausesMilliseconds = [];
    private int _contention;
    private int _gc;
    private double? _gcPauseStartedAt;
    public int EventCount { get; private set; }
    public int SampleCount { get; private set; }

    public void Accept(TraceEvent data, IReadOnlyList<string> stack)
    {
        string provider = data.ProviderName ?? string.Empty;
        string name = data.EventName ?? string.Empty;
        Accept(provider, name, stack, Payload(data, "ExceptionType") ?? Payload(data, "ExceptionTypeName"), data.TimeStampRelativeMSec);
    }

    public void Accept(string provider, string name, IReadOnlyList<string> stack, string? exceptionType = null, double timestampMilliseconds = 0)
    {
        if (EventCount++ >= maxEvents)
        {
            return;
        }
        if (provider.Contains("SampleProfiler", StringComparison.OrdinalIgnoreCase) || name.Contains("ThreadSample", StringComparison.OrdinalIgnoreCase))
        {
            SampleCount++;
            foreach (string? frame in stack.Take(64))
            {
                Increment(_frames, frame);
            }
        }
        if (name.Contains("Contention", StringComparison.OrdinalIgnoreCase))
        {
            _contention++;
        }

        if (name.Contains("Exception", StringComparison.OrdinalIgnoreCase) && !name.Contains("Catch", StringComparison.OrdinalIgnoreCase))
        {
            string type = exceptionType ?? name;
            Increment(_exceptions, type);
        }
        if (name.Contains("GCStart", StringComparison.OrdinalIgnoreCase))
        {
            _gc++;
        }
        if (name.Contains("SuspendEE", StringComparison.OrdinalIgnoreCase) && (name.Contains("Start", StringComparison.OrdinalIgnoreCase) || name.Contains("Begin", StringComparison.OrdinalIgnoreCase)))
        {
            _gcPauseStartedAt = timestampMilliseconds;
        }
        if (_gcPauseStartedAt.HasValue && name.Contains("RestartEE", StringComparison.OrdinalIgnoreCase) && (name.Contains("Stop", StringComparison.OrdinalIgnoreCase) || name.Contains("End", StringComparison.OrdinalIgnoreCase)))
        {
            double duration = timestampMilliseconds - _gcPauseStartedAt.Value;
            if (duration >= 0 && duration < 600_000)
            {
                _gcPausesMilliseconds.Add(duration);
            }
            _gcPauseStartedAt = null;
        }

        if (name.Contains("ThreadPool", StringComparison.OrdinalIgnoreCase) || name.Contains("WorkerThread", StringComparison.OrdinalIgnoreCase))
        {
            Increment(_threadPool, name);
        }
    }

    public IReadOnlyList<AnalysisObservation> Build(string evidenceId)
    {
        var result = new List<AnalysisObservation>();
        if (_frames.Count > 0)
        {
            result.Add(Observation("cpu-hot-paths", "warning", "high", $"{SampleCount} CPU samples produced {_frames.Count} distinct frames", "Frames are ranked by inclusive appearance across sampled call stacks.", new { samples = SampleCount, frames = Top(_frames, 25) }, evidenceId));
        }

        if (_contention > 0)
        {
            result.Add(Observation("contention-events", "warning", "high", $"{_contention} contention events were recorded", "Runtime contention events indicate synchronization pressure in the captured trace.", new { events = _contention }, evidenceId));
        }

        if (_exceptions.Count > 0)
        {
            result.Add(Observation("exception-events", "warning", "high", $"{_exceptions.Values.Sum()} exception events were recorded", "Exception types are grouped from runtime events; repeated throws may not all reach logs.", new { types = Top(_exceptions, 20) }, evidenceId));
        }

        if (_gcPausesMilliseconds.Count > 0)
        {
            result.Add(Observation("gc-pauses", "info", "high", $"{_gcPausesMilliseconds.Count} GC suspension pauses totaled {_gcPausesMilliseconds.Sum():F1} ms", "Pause durations are measured from runtime suspend-start to restart-end events in the trace.", new { count = _gcPausesMilliseconds.Count, totalMilliseconds = _gcPausesMilliseconds.Sum(), maximumMilliseconds = _gcPausesMilliseconds.Max(), gcStarts = _gc }, evidenceId));
        }
        else if (_gc > 0)
        {
            result.Add(Observation("gc-events", "info", "medium", $"{_gc} GC start events were recorded", "Event counts identify GC activity but do not by themselves establish pause duration.", new { events = _gc }, evidenceId));
        }

        if (_threadPool.Count > 0)
        {
            result.Add(Observation("thread-pool-events", "warning", "medium", $"{_threadPool.Values.Sum()} thread-pool events were recorded", "Thread-pool event kinds are grouped for comparison with queue counters and blocked stacks.", new { events = Top(_threadPool, 20) }, evidenceId));
        }

        return result;
    }

    private static AnalysisObservation Observation(string code, string severity, string confidence, string title, string summary, object data, string evidenceId) => new($"obs-{Guid.NewGuid():N}", "trace", code, severity, confidence, title, summary, [evidenceId], data);
    private static object[] Top(Dictionary<string, int> values, int count) => [.. values.OrderByDescending(x => x.Value).ThenBy(x => x.Key, StringComparer.Ordinal).Take(count).Select(x => (object)new { name = x.Key, count = x.Value })];
    private static void Increment(Dictionary<string, int> values, string key) { if (string.IsNullOrWhiteSpace(key)) { return; } values[key] = values.GetValueOrDefault(key) + 1; }
    private static string? Payload(TraceEvent data, string name) { try { return data.PayloadByName(name)?.ToString(); } catch { return null; } }
}
