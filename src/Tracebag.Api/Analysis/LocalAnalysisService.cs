using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tracebag.Api.Artifacts;
using Tracebag.Api.Audit;
using Tracebag.Api.Data;
using Tracebag.Api.Models;
using Tracebag.Api.Security;

namespace Tracebag.Api.Analysis;

public sealed class LocalAnalysisService(
    IDbContextFactory<TracebagDbContext> dbFactory,
    ArtifactStore artifacts,
    StackSnapshotAnalyzer stacks,
    NetTraceAnalyzer traces,
    AuditLog audit)
{
    private const string AnalyzerVersion = "tracebag-local/1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<AnalysisRunDto> AnalyzeAsync(string incidentId, string user, CancellationToken cancellationToken)
    {
        AnalysisRunRecord run;
        await using (TracebagDbContext db = await dbFactory.CreateDbContextAsync(cancellationToken))
        {
            IncidentRecord incident = await db.Incidents.AsNoTracking().FirstOrDefaultAsync(x => x.Id == incidentId, cancellationToken)
                ?? throw new TracebagException(404, "incident_not_found", "The incident was not found.");
            if (await db.AnalysisRuns.AnyAsync(x => x.IncidentId == incidentId && x.Status == "running", cancellationToken))
            {
                throw new TracebagException(409, "analysis_already_running", "A local analysis is already running for this incident.");
            }
            run = new AnalysisRunRecord
            {
                Id = $"analysis-{Guid.NewGuid():N}", IncidentId = incidentId, EnvelopeVersion = 1,
                AnalyzerVersion = AnalyzerVersion, Status = "running", CreatedBy = string.IsNullOrWhiteSpace(user) ? "anonymous" : user,
                CreatedAt = DateTimeOffset.UtcNow
            };
            db.AnalysisRuns.Add(run);
            db.IncidentTimeline.Add(new IncidentTimelineRecord { IncidentId = incidentId, Timestamp = run.CreatedAt, Type = "analysis", Severity = "info", Title = "Local analysis started", Summary = $"{AnalyzerVersion} is analyzing bounded evidence locally." });
            await db.SaveChangesAsync(cancellationToken);
        }

        try
        {
            return await ExecuteAsync(run, cancellationToken);
        }
        catch (Exception ex)
        {
            await using TracebagDbContext db = await dbFactory.CreateDbContextAsync(CancellationToken.None);
            AnalysisRunRecord? failed = await db.AnalysisRuns.FindAsync([run.Id], CancellationToken.None);
            if (failed is not null)
            {
                failed.Status = "failed"; failed.CompletedAt = DateTimeOffset.UtcNow; failed.ErrorMessage = SafeError(ex);
                db.IncidentTimeline.Add(new IncidentTimelineRecord { IncidentId = incidentId, Timestamp = failed.CompletedAt.Value, Type = "analysis", Severity = "error", Title = "Local analysis failed", Summary = failed.ErrorMessage });
                await db.SaveChangesAsync(CancellationToken.None);
            }
            throw;
        }
    }

    public async Task<AnalysisRunDto?> LatestAsync(string incidentId, CancellationToken cancellationToken)
    {
        await using TracebagDbContext db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (!await db.Incidents.AnyAsync(x => x.Id == incidentId, cancellationToken))
        {
            throw new TracebagException(404, "incident_not_found", "The incident was not found.");
        }

        AnalysisRunRecord? run = await db.AnalysisRuns.AsNoTracking().Where(x => x.IncidentId == incidentId).OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        return run is null ? null : ToDto(run);
    }

    public async Task RecoverAsync(CancellationToken cancellationToken)
    {
        await using TracebagDbContext db = await dbFactory.CreateDbContextAsync(cancellationToken);
        AnalysisRunRecord[] interrupted = await db.AnalysisRuns.Where(x => x.Status == "running").ToArrayAsync(cancellationToken);
        foreach (AnalysisRunRecord? run in interrupted)
        {
            run.Status = "failed"; run.CompletedAt = DateTimeOffset.UtcNow; run.ErrorMessage = "Tracebag restarted while local analysis was running. The incident evidence remains intact and analysis can be rerun.";
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<AnalysisRunDto> ExecuteAsync(AnalysisRunRecord run, CancellationToken cancellationToken)
    {
        IncidentRecord incident;
        IncidentEvidenceRecord[] records;
        await using (TracebagDbContext db = await dbFactory.CreateDbContextAsync(cancellationToken))
        {
            incident = await db.Incidents.AsNoTracking().SingleAsync(x => x.Id == run.IncidentId, cancellationToken);
            records = await db.IncidentEvidence.AsNoTracking().Where(x => x.IncidentId == run.IncidentId).OrderBy(x => x.CapturedAt).ToArrayAsync(cancellationToken);
        }
        IncidentEvidenceDto[] evidence = records.Select(ToEvidence).ToArray();
        var components = new List<AnalysisComponent>();
        var observations = new List<AnalysisObservation>();
        var limitations = new List<AnalysisLimitation>();

        await AnalyzeArtifactsAsync("stack", evidence.Where(IsStack), async (item, path) => await stacks.AnalyzeAsync(path, item, cancellationToken), components, observations, limitations, cancellationToken);
        await AnalyzeArtifactsAsync("trace", evidence.Where(IsTrace), async (item, path) => await traces.AnalyzeAsync(path, item, cancellationToken), components, observations, limitations, cancellationToken);
        AddSignalAnalysis(evidence, components, observations, limitations);
        List<AnalysisCorrelation> correlations = Correlate(observations);

        var envelope = new AnalysisEnvelope(
            1, AnalyzerVersion, run.IncidentId, DateTimeOffset.UtcNow,
            new AnalysisWindow(incident.WindowStart, incident.WindowEnd ?? DateTimeOffset.UtcNow),
            [.. evidence.Select(x => new AnalysisSource(x.Id, x.Kind, x.Title, x.ArtifactId))],
            components, observations, correlations, limitations,
            new AnalysisDisclosure(true, false, false));
        int failedComponents = components.Count(x => x.Status == "failed");
        string status = failedComponents == 0 ? "completed" : observations.Count > 0 ? "partial" : "failed";

        await using (TracebagDbContext db = await dbFactory.CreateDbContextAsync(cancellationToken))
        {
            AnalysisRunRecord stored = await db.AnalysisRuns.SingleAsync(x => x.Id == run.Id, cancellationToken);
            stored.Status = status; stored.CompletedAt = DateTimeOffset.UtcNow; stored.ErrorMessage = failedComponents == 0 ? null : $"{failedComponents} analyzer component(s) failed; successful findings were preserved.";
            stored.EnvelopeJson = JsonSerializer.Serialize(envelope, JsonOptions);
            foreach (AnalysisObservation observation in observations)
            {
                var finding = new IncidentFindingRecord { Id = $"finding-{Guid.NewGuid():N}", IncidentId = run.IncidentId, AnalysisRunId = run.Id, Code = observation.Code, Severity = observation.Severity, Confidence = observation.Confidence, Title = observation.Title, Summary = observation.Summary, CreatedAt = stored.CompletedAt.Value };
                db.IncidentFindings.Add(finding);
                db.IncidentFindingEvidence.AddRange(observation.EvidenceIds.Distinct().Select(id => new IncidentFindingEvidenceRecord { FindingId = finding.Id, EvidenceId = id }));
            }
            db.IncidentTimeline.Add(new IncidentTimelineRecord { IncidentId = run.IncidentId, Timestamp = stored.CompletedAt.Value, Type = "analysis", Severity = status == "completed" ? "info" : "warning", Title = status == "completed" ? "Local analysis completed" : "Local analysis completed with limitations", Summary = $"{observations.Count} observations and {correlations.Count} cross-signal correlations produced; {failedComponents} components failed." });
            await db.SaveChangesAsync(cancellationToken);
            run = stored;
        }
        await audit.WriteAsync(run.CreatedBy, "incident.analysis", incident.ContainerId, incident.ContainerName, status, new { run.Id, run.IncidentId, observations = observations.Count, correlations = correlations.Count, failedComponents, externalProvidersUsed = false }, cancellationToken);
        return ToDto(run);
    }

    private async Task AnalyzeArtifactsAsync(
        string name, IEnumerable<IncidentEvidenceDto> candidates,
        Func<IncidentEvidenceDto, string, Task<AnalyzerOutput>> analyze,
        List<AnalysisComponent> components, List<AnalysisObservation> observations, List<AnalysisLimitation> limitations,
        CancellationToken cancellationToken)
    {
        IncidentEvidenceDto[] items = candidates.ToArray();
        if (items.Length == 0)
        {
            components.Add(new AnalysisComponent(name, "skipped", 0, 0, null));
            limitations.Add(new AnalysisLimitation($"{name}-evidence-unavailable", $"No {name} artifact was selected during capture.", null));
            return;
        }
        foreach (IncidentEvidenceDto? item in items)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                (ArtifactMetadata _, string? path) = await artifacts.GetForDownloadAsync(item.ArtifactId!, cancellationToken);
                AnalyzerOutput output = await analyze(item, path);
                components.Add(output.Component); observations.AddRange(output.Observations); limitations.AddRange(output.Limitations);
            }
            catch (Exception ex)
            {
                string error = SafeError(ex);
                components.Add(AnalyzerTiming.Component(name, "failed", stopwatch, 0, error));
                limitations.Add(new AnalysisLimitation($"{name}-analysis-failed", $"This artifact could not be analyzed: {error}", item.Id));
            }
        }
    }

    private static void AddSignalAnalysis(IReadOnlyList<IncidentEvidenceDto> evidence, List<AnalysisComponent> components, List<AnalysisObservation> observations, List<AnalysisLimitation> limitations)
    {
        var stopwatch = Stopwatch.StartNew();
        foreach (IncidentEvidenceDto item in evidence)
        {
            JsonElement json = JsonSerializer.SerializeToElement(item.Summary, JsonOptions);
            if (item.Kind == "logs" && json.TryGetProperty("errors", out JsonElement errors) && errors.TryGetInt32(out int errorCount) && errorCount > 0)
            {
                observations.Add(Signal("log-errors", "warning", "high", $"{errorCount} error or exception logs were pinned", "The count is bounded to the incident log evidence window.", item.Id, new { count = errorCount }));
            }

            if (item.Kind == "docker-snapshot")
            {
                double cpu = Number(json, "cpuPercent"); double memory = Number(json, "memoryPercent");
                if (cpu >= 80)
                {
                    observations.Add(Signal("docker-cpu-pressure", "warning", "high", $"Container CPU was {cpu:F1}% at capture start", "This is an instantaneous Docker sample and should be compared with runtime counters and CPU samples.", item.Id, new { cpuPercent = cpu }));
                }

                if (memory >= 85)
                {
                    observations.Add(Signal("docker-memory-pressure", "warning", "high", $"Container memory was {memory:F1}% of its limit", "This is an instantaneous Docker sample and should be compared with GC counters.", item.Id, new { memoryPercent = memory }));
                }

                if (json.TryGetProperty("oomKilled", out JsonElement oom) && oom.ValueKind == JsonValueKind.True)
                {
                    observations.Add(Signal("docker-oom-killed", "critical", "high", "Docker reports an OOM kill", "The container state records an out-of-memory termination.", item.Id, null));
                }
            }
            if (item.Kind == "counter-window" && json.TryGetProperty("peaks", out JsonElement peaks) && peaks.ValueKind == JsonValueKind.Array)
            {
                var pressure = peaks.EnumerateArray().Where(x => Number(x, "maximum") > 0 && CounterIsPressure(x.TryGetProperty("name", out JsonElement n) ? n.GetString() : null)).Take(20)
                    .Select(x => new { name = x.GetProperty("name").GetString(), maximum = Number(x, "maximum"), average = Number(x, "average") }).ToArray();
                if (pressure.Length > 0)
                {
                    observations.Add(Signal("runtime-counter-pressure", "warning", "medium", $"{pressure.Length} pressure counters peaked above zero", "Counter peaks are retained with their incident-window averages for correlation.", item.Id, new { counters = pressure }));
                }
            }
        }
        components.Add(AnalyzerTiming.Component("signals", "completed", stopwatch, observations.Count(x => x.Analyzer == "signals")));
        if (!evidence.Any(x => x.Kind == "counter-window"))
        {
            limitations.Add(new AnalysisLimitation("counter-evidence-unavailable", "No readable counter series was available for cross-signal analysis.", null));
        }
    }

    private static List<AnalysisCorrelation> Correlate(IReadOnlyList<AnalysisObservation> observations)
    {
        var result = new List<AnalysisCorrelation>();
        Add("cpu-signals-align", "high", "CPU samples and container/runtime pressure agree in the same incident window.", ["cpu-hot-paths"], ["docker-cpu-pressure", "runtime-counter-pressure"]);
        Add("contention-signals-align", "high", "Runtime contention events align with blocked stacks or pressure counters.", ["contention-events"], ["blocked-thread-stacks", "runtime-counter-pressure"]);
        Add("exceptions-align-with-logs", "high", "Runtime exception events align with error or exception logs in the bounded window.", ["exception-events"], ["log-errors"]);
        Add("threading-signals-align", "medium", "Thread-pool events align with blocked stacks or runtime pressure counters.", ["thread-pool-events"], ["blocked-thread-stacks", "runtime-counter-pressure"]);
        Add("gc-pressure-aligns", "high", "Measured GC pauses align with managed-runtime or container memory pressure.", ["gc-pauses"], ["runtime-counter-pressure", "docker-memory-pressure"]);
        return result;

        void Add(string code, string confidence, string summary, string[] left, string[] right)
        {
            AnalysisObservation[] matches = observations.Where(x => left.Contains(x.Code) || right.Contains(x.Code)).ToArray();
            if (matches.Any(x => left.Contains(x.Code)) && matches.Any(x => right.Contains(x.Code)))
            {
                result.Add(new AnalysisCorrelation(code, confidence, summary, [.. matches.Select(x => x.Id)], [.. matches.SelectMany(x => x.EvidenceIds).Distinct()]));
            }
        }
    }

    private static AnalysisObservation Signal(string code, string severity, string confidence, string title, string summary, string evidenceId, object? data) => new($"obs-{Guid.NewGuid():N}", "signals", code, severity, confidence, title, summary, [evidenceId], data);
    private static bool CounterIsPressure(string? name) => name?.Contains("queue", StringComparison.OrdinalIgnoreCase) == true || name?.Contains("contention", StringComparison.OrdinalIgnoreCase) == true || name?.Contains("time-in-gc", StringComparison.OrdinalIgnoreCase) == true || name?.Contains("failed", StringComparison.OrdinalIgnoreCase) == true;
    private static double Number(JsonElement value, string property) => value.TryGetProperty(property, out JsonElement item) && item.TryGetDouble(out double number) ? number : 0;
    private static bool IsStack(IncidentEvidenceDto item) => item.ArtifactId is not null && item.Title.Contains("stack", StringComparison.OrdinalIgnoreCase);
    private static bool IsTrace(IncidentEvidenceDto item) => item.ArtifactId is not null && (item.Title.Contains("trace", StringComparison.OrdinalIgnoreCase) || item.Title.Contains("cpu-sampling", StringComparison.OrdinalIgnoreCase) || item.Title.Contains("contention", StringComparison.OrdinalIgnoreCase) || item.Title.Contains("threading", StringComparison.OrdinalIgnoreCase));
    private static string SafeError(Exception exception) { string value = exception.Message.Replace('\r', ' ').Replace('\n', ' ').Trim(); return value.Length > 1000 ? value[..1000] : value; }
    private static IncidentEvidenceDto ToEvidence(IncidentEvidenceRecord x) => new(x.Id, x.Kind, x.Title, x.CapturedAt, x.From, x.To, x.SourceId, x.ArtifactId, JsonSerializer.Deserialize<object>(x.SummaryJson, JsonOptions)!, JsonSerializer.Deserialize<object>(x.PayloadJson, JsonOptions)!, x.SelectedByDefault, x.Sensitive, x.RedactionStatus);
    internal static AnalysisRunDto ToDto(AnalysisRunRecord x) => new(x.Id, x.IncidentId, x.EnvelopeVersion, x.AnalyzerVersion, x.Status, x.CreatedBy, x.CreatedAt, x.CompletedAt, x.ErrorMessage, string.IsNullOrWhiteSpace(x.EnvelopeJson) ? null : JsonSerializer.Deserialize<AnalysisEnvelope>(x.EnvelopeJson, JsonOptions));
}

public sealed class LocalAnalysisRecoveryService(LocalAnalysisService analyses) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => analyses.RecoverAsync(cancellationToken);
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
