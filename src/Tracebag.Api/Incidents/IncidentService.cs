using System.Collections.Concurrent;
using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tracebag.Api.Artifacts;
using Tracebag.Api.Analysis;
using Tracebag.Api.Audit;
using Tracebag.Api.Auth;
using Tracebag.Api.Data;
using Tracebag.Api.Diagnostics;
using Tracebag.Api.Docker;
using Tracebag.Api.Logs;
using Tracebag.Api.Models;
using Tracebag.Api.Security;

namespace Tracebag.Api.Incidents;

public sealed class IncidentService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] ActiveStatuses = ["queued", "collecting", "analyzing"];
    private static readonly string[] EditableStatuses = ["ready", "partial", "closed"];
    private readonly IDbContextFactory<TracebagDbContext> _dbFactory;
    private readonly GuidedIncidentProfileCatalog _profiles;
    private readonly ContainerOperationalService _containers;
    private readonly CounterRecordingService _recordings;
    private readonly DiagnosticJobService _jobs;
    private readonly LogStore _logs;
    private readonly AuditLog _audit;
    private readonly LocalAnalysisService _analysis;
    private readonly TracebagOptions _options;
    private readonly ConcurrentDictionary<string, Task> _workers = new();

    public IncidentService(
        IDbContextFactory<TracebagDbContext> dbFactory,
        GuidedIncidentProfileCatalog profiles,
        ContainerOperationalService containers,
        CounterRecordingService recordings,
        DiagnosticJobService jobs,
        LogStore logs,
        AuditLog audit,
        LocalAnalysisService analysis,
        TracebagOptions options)
    {
        _dbFactory = dbFactory; _profiles = profiles; _containers = containers; _recordings = recordings; _jobs = jobs; _logs = logs; _audit = audit; _analysis = analysis; _options = options;
    }

    public IReadOnlyList<GuidedIncidentProfileDto> Profiles() => _profiles.List();

    public async Task<IReadOnlyList<IncidentSummaryDto>> ListAsync(string? containerId, string? status, CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var query = db.Incidents.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(containerId))
        {
            query = query.Where(x => x.ContainerId == containerId);
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(x => x.Status == status);
        }
        return (await query.OrderByDescending(x => x.CreatedAt).Take(100).ToArrayAsync(cancellationToken)).Select(ToSummary).ToArray();
    }

    public async Task<IncidentDetailDto> GetAsync(string id, CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var incident = await db.Incidents.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw NotFound();
        var timeline = await db.IncidentTimeline.AsNoTracking().Where(x => x.IncidentId == id).OrderBy(x => x.Timestamp).ThenBy(x => x.Id).ToArrayAsync(cancellationToken);
        var evidence = await db.IncidentEvidence.AsNoTracking().Where(x => x.IncidentId == id).OrderBy(x => x.CapturedAt).ToArrayAsync(cancellationToken);
        var analysis = await db.AnalysisRuns.AsNoTracking().Where(x => x.IncidentId == id).OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        var latestAnalysisId = analysis == null ? null : analysis.Id;
        var findings = await db.IncidentFindings.AsNoTracking().Where(x => x.IncidentId == id && (x.AnalysisRunId == null || x.AnalysisRunId == latestAnalysisId)).OrderByDescending(x => x.CreatedAt).ToArrayAsync(cancellationToken);
        var links = await db.IncidentFindingEvidence.AsNoTracking().Where(x => findings.Select(f => f.Id).Contains(x.FindingId)).ToArrayAsync(cancellationToken);
        return new IncidentDetailDto(ToSummary(incident), timeline.Select(ToTimeline).ToArray(), evidence.Select(ToEvidence).ToArray(), findings.Select(f => ToFinding(f, links)).ToArray(), analysis is null ? null : LocalAnalysisService.ToDto(analysis));
    }

    public async Task<IncidentSummaryDto> CreateAsync(string containerReference, IncidentCreateRequest request, string user, CancellationToken cancellationToken)
    {
        if (request.ProcessId <= 0)
        {
            throw new TracebagException(400, "process_id_invalid", "A valid .NET process id is required.");
        }
        var profile = _profiles.Get(request.Profile);
        var overview = await _containers.GetOverviewAsync(containerReference, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var seconds = Math.Clamp(request.CaptureSeconds ?? profile.DefaultCaptureSeconds, 10, 120);
        var incident = new IncidentRecord
        {
            Id = $"inc-{Guid.NewGuid():N}",
            ContainerId = overview.Container.Id,
            ContainerName = overview.Container.DisplayName,
            DockerId = overview.Container.DockerId,
            ProcessId = request.ProcessId,
            Title = Normalize(request.Title, 200) ?? $"{profile.DisplayName} · {overview.Container.DisplayName}",
            Profile = profile.Id,
            Reason = Normalize(request.Reason, 2000),
            Status = "queued",
            Progress = 0,
            CreatedBy = string.IsNullOrWhiteSpace(user) ? "anonymous" : user,
            CreatedAt = now,
            WindowStart = now.AddMinutes(-2),
            CaptureOptionsJson = JsonSerializer.Serialize(new { captureSeconds = seconds, request.IncludeTrace }, JsonOptions)
        };
        await using (var db = await _dbFactory.CreateDbContextAsync(cancellationToken))
        {
            await using var transaction = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
                : null;
            if (db.Database.IsNpgsql())
            {
                await db.Database.ExecuteSqlRawAsync(
                    "LOCK TABLE incidents IN SHARE ROW EXCLUSIVE MODE",
                    cancellationToken);
            }

            EnsureCapacity(await db.Incidents.CountAsync(cancellationToken), _options.IncidentMaxCount);

            if (await db.Incidents.AnyAsync(x => x.ContainerId == incident.ContainerId && ActiveStatuses.Contains(x.Status), cancellationToken))
            {
                throw new TracebagException(409, "incident_already_active", "There is already an active incident capture for this target.");
            }
            db.Incidents.Add(incident);
            db.IncidentTimeline.Add(Event(incident.Id, now, "incident", "info", "Incident created", $"Guided {profile.DisplayName} capture queued."));
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                if (transaction is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }
            }
            catch (DbUpdateException) { throw new TracebagException(409, "incident_already_active", "There is already an active incident capture for this target."); }
        }
        await _audit.WriteAsync(user, "incident.create", incident.ContainerId, incident.ContainerName, "accepted", new { incident.Id, profile = profile.Id, seconds, request.IncludeTrace }, cancellationToken);
        _workers[incident.Id] = Task.Run(async () =>
        {
            try { await CaptureAsync(incident.Id, containerReference, profile, seconds, request.IncludeTrace, user); }
            finally { _workers.TryRemove(incident.Id, out _); }
        }, CancellationToken.None);
        return ToSummary(incident);
    }

    public async Task<IncidentDeleteResult> DeleteAsync(
        string id,
        string? confirmation,
        string user,
        CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var incident = await db.Incidents.FirstOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw NotFound();
        if (ActiveStatuses.Contains(incident.Status)
            || _workers.ContainsKey(id)
            || await db.AnalysisRuns.AnyAsync(run => run.IncidentId == id && run.Status == "running", cancellationToken))
        {
            throw new TracebagException(
                StatusCodes.Status409Conflict,
                "incident_delete_active",
                "Wait for incident capture and analysis to finish before deleting it.");
        }

        if (!string.Equals(confirmation, id, StringComparison.Ordinal))
        {
            throw new TracebagException(
                StatusCodes.Status400BadRequest,
                "incident_delete_confirmation_required",
                "Deletion requires the exact incident id in the confirm query parameter.");
        }

        var timelineEvents = await db.IncidentTimeline.CountAsync(item => item.IncidentId == id, cancellationToken);
        var evidence = await db.IncidentEvidence.Where(item => item.IncidentId == id).ToArrayAsync(cancellationToken);
        var findings = await db.IncidentFindings.CountAsync(item => item.IncidentId == id, cancellationToken);
        var analysisRuns = await db.AnalysisRuns.CountAsync(item => item.IncidentId == id, cancellationToken);
        var releasedArtifacts = evidence
            .Where(item => item.ArtifactId is not null)
            .Select(item => item.ArtifactId!)
            .Distinct(StringComparer.Ordinal)
            .Count();
        var releasedRecordings = evidence
            .Where(item => item.Kind == "counter-window" && item.SourceId is not null)
            .Select(item => item.SourceId!)
            .Distinct(StringComparer.Ordinal)
            .Count();
        var releasedJobs = evidence
            .Where(item => item.Kind == "diagnostic-artifact" && item.SourceId is not null)
            .Select(item => item.SourceId!)
            .Distinct(StringComparer.Ordinal)
            .Count();

        db.Incidents.Remove(incident);
        await db.SaveChangesAsync(cancellationToken);

        var result = new IncidentDeleteResult(
            id,
            "deleted",
            timelineEvents,
            evidence.Length,
            findings,
            analysisRuns,
            releasedJobs,
            releasedRecordings,
            releasedArtifacts);
        await _audit.WriteAsync(user, "incident.delete", incident.ContainerId, incident.ContainerName, "success", result, cancellationToken);
        return result;
    }

    public async Task<IncidentSummaryDto> UpdateAsync(string id, IncidentUpdateRequest request, string user, CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var incident = await db.Incidents.FirstOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw NotFound();
        if (request.Notes is not null)
        {
            incident.Notes = Normalize(request.Notes, 8000);
            db.IncidentTimeline.Add(Event(id, DateTimeOffset.UtcNow, "note", "info", "Notes updated", "Incident notes were updated."));
        }
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var status = request.Status.Trim().ToLowerInvariant();
            if (status != "closed" || !EditableStatuses.Contains(incident.Status))
            {
                throw new TracebagException(409, "incident_status_invalid", "Only a completed incident can be closed.");
            }
            incident.Status = "closed";
            db.IncidentTimeline.Add(Event(id, DateTimeOffset.UtcNow, "incident", "info", "Incident closed", $"Closed by {user}."));
        }
        await db.SaveChangesAsync(cancellationToken);
        return ToSummary(incident);
    }

    public async Task RecoverAsync(CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var interrupted = await db.Incidents.Where(x => ActiveStatuses.Contains(x.Status)).ToArrayAsync(cancellationToken);
        foreach (var item in interrupted)
        {
            item.Status = "partial"; item.CompletedAt = DateTimeOffset.UtcNow; item.WindowEnd = item.CompletedAt; item.ErrorMessage = "Tracebag restarted during capture; collected evidence was preserved.";
            db.IncidentTimeline.Add(Event(item.Id, item.CompletedAt.Value, "incident", "warning", "Capture interrupted", item.ErrorMessage));
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task CaptureAsync(string incidentId, string containerReference, GuidedIncidentProfileDto profile, int seconds, bool includeTrace, string user)
    {
        var failures = new List<string>(); string? recordingId = null;
        try
        {
            await SetProgressAsync(incidentId, "collecting", 10, "Capture started", "Docker state, counters, logs and diagnostics are being correlated.");
            var overview = await _containers.GetOverviewAsync(containerReference, CancellationToken.None);
            await AddEvidenceAsync(incidentId, "docker-snapshot", "Docker state at capture start", overview.Resources.ReadAt ?? DateTimeOffset.UtcNow, null, null, null, null,
                new { overview.Inspect.Running, overview.Inspect.OomKilled, overview.Inspect.RestartCount, overview.Inspect.Health.Status, overview.Resources.CpuPercent, overview.Resources.MemoryPercent, overview.Resources.Pids }, overview, true, false, "not-required");
            try
            {
                var started = await _recordings.StartAsync(containerReference, new CounterRecordingStartRequest(await ProcessIdAsync(incidentId), profile.CounterPreset, 2, 1, $"Incident {incidentId}"), user, CancellationToken.None);
                recordingId = started.Id;
                await TimelineAsync(incidentId, "counter", "info", "Counter window started", $"Preset {profile.CounterPreset}, recording {recordingId}.");
            }
            catch (Exception ex) { failures.Add($"Counters: {ex.Message}"); await TimelineAsync(incidentId, "counter", "warning", "Counters unavailable", ex.Message); }

            var primary = await RunDiagnosticAsync(incidentId, containerReference, profile.PrimaryDiagnostic, profile.PrimaryDiagnostic.Contains("trace", StringComparison.Ordinal) ? seconds : null, user);
            if (primary is null)
            {
                failures.Add($"Diagnostic {profile.PrimaryDiagnostic} failed.");
            }
            await SetProgressAsync(incidentId, "collecting", 50, "Core evidence captured", "Completing the bounded observation window.");
            var elapsed = DateTimeOffset.UtcNow - (await IncidentAsync(incidentId)).CreatedAt;
            if (elapsed < TimeSpan.FromSeconds(seconds))
            {
                await Task.Delay(TimeSpan.FromSeconds(seconds) - elapsed);
            }
            if (recordingId is not null)
            {
                try
                {
                    await _recordings.StopAsync(recordingId, user, CancellationToken.None);
                    for (var attempt = 0; attempt < 12; attempt++)
                    {
                        var recording = await _recordings.GetDetailAsync(recordingId, CancellationToken.None);
                        if (recording.Recording.SampleCount > 0)
                        {
                            break;
                        }

                        await Task.Delay(250);
                    }
                    var samples = await _recordings.GetSamplesAsync(recordingId, null, null, "raw", CancellationToken.None);
                    if (samples.Series.Count == 0)
                    {
                        failures.Add("Counter window completed without readable samples.");
                        await TimelineAsync(incidentId, "counter", "warning", "Counter window has no samples", "The runner completed, but no counter samples were available after the bounded flush wait.");
                    }
                    await AddEvidenceAsync(incidentId, "counter-window", $"{profile.CounterPreset} counter window", DateTimeOffset.UtcNow, (await IncidentAsync(incidentId)).WindowStart, DateTimeOffset.UtcNow, recordingId, null,
                        new { recordingId, preset = profile.CounterPreset, seriesCount = samples.Series.Count, peaks = samples.Series.Select(x => new { x.Name, x.Summary.Maximum, x.Summary.PeakAt, x.Summary.Average }) }, samples, true, false, "not-required");
                }
                catch (Exception ex) { failures.Add($"Counter finalization: {ex.Message}"); await TimelineAsync(incidentId, "counter", "warning", "Counter window incomplete", ex.Message); }
            }
            if (includeTrace && profile.OptionalTrace is not null)
            {
                if (await RunDiagnosticAsync(incidentId, containerReference, profile.OptionalTrace, Math.Min(seconds, 60), user) is null)
                {
                    failures.Add($"Optional trace {profile.OptionalTrace} failed.");
                }
            }
            var incident = await IncidentAsync(incidentId);
            var end = DateTimeOffset.UtcNow;
            try
            {
                var logs = await _logs.SearchAsync(incident.ContainerId, new LogSearchRequest(null, null, null, false, null, incident.WindowStart, end, null, 200), CancellationToken.None);
                await AddEvidenceAsync(incidentId, "logs", "Pinned logs in the incident window", end, incident.WindowStart, end, null, null,
                    new { count = logs.Items.Count, logs.HasMore, boundedLimit = 200, errors = logs.Items.Count(x => x.Level is "error" or "critical" || x.ExceptionType is not null) }, logs.Items, false, true, "not-redacted");
            }
            catch (Exception ex) { failures.Add($"Logs: {ex.Message}"); await TimelineAsync(incidentId, "logs", "warning", "Pinned logs unavailable", ex.Message); }
            await SetProgressAsync(incidentId, "analyzing", 90, "Analyzing local evidence", "Applying deterministic local heuristics with evidence references.");
            await AnalyzeAsync(incidentId, profile);
            try
            {
                await _analysis.AnalyzeAsync(incidentId, user, CancellationToken.None);
            }
            catch (Exception ex)
            {
                failures.Add($"Local analysis: {ex.Message}");
                await TimelineAsync(incidentId, "analysis", "warning", "Local analysis unavailable", "Captured evidence remains intact and analysis can be rerun.");
            }
            await CompleteAsync(incidentId, failures);
            await _audit.WriteAsync(user, "incident.capture.complete", incident.ContainerId, incident.ContainerName, failures.Count == 0 ? "success" : "partial", new { incidentId, failures }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            if (recordingId is not null) { try { await _recordings.StopAsync(recordingId, user, CancellationToken.None); } catch { } }
            await FailAsync(incidentId, ex.Message);
        }
    }

    private async Task<DiagnosticJobResponse?> RunDiagnosticAsync(string incidentId, string containerReference, string profile, int? duration, string user)
    {
        try
        {
            var job = await _jobs.CreateAsync(containerReference, new DiagnosticJobCreateRequest(await ProcessIdAsync(incidentId), profile, duration, null), $"{incidentId}-{profile}", user, CancellationToken.None);
            await TimelineAsync(incidentId, "diagnostic", "info", $"{profile} started", $"Diagnostic job {job.Id}.");
            var deadline = DateTimeOffset.UtcNow.AddMinutes(5);
            while (!DiagnosticJobStore.IsTerminal(job.Status) && DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(500); job = await _jobs.GetAsync(job.Id, CancellationToken.None);
            }
            if (job.Status != "completed" || string.IsNullOrWhiteSpace(job.ArtifactId))
            {
                await TimelineAsync(incidentId, "diagnostic", "warning", $"{profile} incomplete", job.ErrorMessage ?? job.Status); return null;
            }
            var evidenceId = await AddEvidenceAsync(incidentId, "diagnostic-artifact", $"{profile} artifact", job.CompletedAt ?? DateTimeOffset.UtcNow, job.StartedAt, job.CompletedAt, job.Id, job.ArtifactId,
                new { job.Id, job.Profile, job.Status, job.ArtifactId, job.Outcome }, new { job.Inputs, job.Outcome }, false, profile == "full-dump", profile == "full-dump" ? "not-redacted-sensitive" : "not-redacted");
            await TimelineAsync(incidentId, "artifact", "info", $"{profile} captured", $"Artifact {job.ArtifactId} passed integrity registration.", evidenceId);
            return job;
        }
        catch (Exception ex) { await TimelineAsync(incidentId, "diagnostic", "warning", $"{profile} unavailable", ex.Message); return null; }
    }

    private async Task AnalyzeAsync(string incidentId, GuidedIncidentProfileDto profile)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var evidence = await db.IncidentEvidence.Where(x => x.IncidentId == incidentId).ToArrayAsync();
        var logs = evidence.FirstOrDefault(x => x.Kind == "logs"); var counters = evidence.FirstOrDefault(x => x.Kind == "counter-window"); var docker = evidence.FirstOrDefault(x => x.Kind == "docker-snapshot");
        var artifact = evidence.FirstOrDefault(x => x.Kind == "diagnostic-artifact");
        var candidates = new List<(string Code, string Severity, string Confidence, string Title, string Summary, string[] Evidence)>
        {
            profile.Id switch
            {
                "frozen-api" => ("thread-pool-starvation", "warning", "medium", "Thread-pool starvation evidence captured", "Thread-pool counters and a stack snapshot were correlated over the incident window. Inspect queue peaks and blocked stacks together.", Existing(counters, artifact, logs)),
                "high-cpu" => ("cpu-pressure", "warning", "medium", "CPU pressure evidence captured", "Container CPU state and runtime/stack evidence are aligned to the same capture window.", Existing(docker, counters, artifact)),
                "high-memory" => ("gc-pressure", "warning", "medium", "Managed memory pressure evidence captured", "GC counter peaks and the managed heap graph can be compared without a full process dump.", Existing(docker, counters, artifact)),
                "request-timeouts" => ("request-timeouts", "warning", "medium", "Request timeout evidence captured", "Request counters and bounded logs are aligned with scheduling evidence.", Existing(counters, logs, artifact)),
                _ => ("lock-contention", "warning", "medium", "Lock-contention evidence captured", "Monitor contention counters and managed stacks are correlated; optional contention events add timing detail.", Existing(counters, artifact, logs))
            }
        };
        if (logs is not null)
        {
            using var document = JsonDocument.Parse(logs.SummaryJson);
            if (document.RootElement.TryGetProperty("errors", out var errors) && errors.GetInt32() > 0)
            {
                candidates.Add(("exceptions-in-window", "warning", "high", "Errors occurred in the capture window", $"{errors.GetInt32()} error or exception log entries were pinned.", [logs.Id]));
            }
        }
        foreach (var item in candidates.Where(x => x.Evidence.Length > 0))
        {
            var finding = new IncidentFindingRecord { Id = $"finding-{Guid.NewGuid():N}", IncidentId = incidentId, Code = item.Code, Severity = item.Severity, Confidence = item.Confidence, Title = item.Title, Summary = item.Summary, CreatedAt = DateTimeOffset.UtcNow };
            db.IncidentFindings.Add(finding);
            db.IncidentFindingEvidence.AddRange(item.Evidence.Select(id => new IncidentFindingEvidenceRecord { FindingId = finding.Id, EvidenceId = id }));
            db.IncidentTimeline.Add(Event(incidentId, finding.CreatedAt, "finding", item.Severity, item.Title, item.Summary, item.Evidence[0]));
        }
        await db.SaveChangesAsync();
    }

    private static string[] Existing(params IncidentEvidenceRecord?[] records) => records.Where(x => x is not null).Select(x => x!.Id).Distinct().ToArray();
    private async Task<string> AddEvidenceAsync(string incidentId, string kind, string title, DateTimeOffset capturedAt, DateTimeOffset? from, DateTimeOffset? to, string? sourceId, string? artifactId, object summary, object payload, bool selected, bool sensitive, string redaction)
    {
        var id = $"evidence-{Guid.NewGuid():N}";
        await using var db = await _dbFactory.CreateDbContextAsync();
        db.IncidentEvidence.Add(new IncidentEvidenceRecord { Id = id, IncidentId = incidentId, Kind = kind, Title = title, CapturedAt = capturedAt, From = from, To = to, SourceId = sourceId, ArtifactId = artifactId, SummaryJson = JsonSerializer.Serialize(summary, JsonOptions), PayloadJson = JsonSerializer.Serialize(payload, JsonOptions), SelectedByDefault = selected, Sensitive = sensitive, RedactionStatus = redaction });
        db.IncidentTimeline.Add(Event(incidentId, capturedAt, "evidence", "info", title, $"Bounded {kind} evidence added.", id)); await db.SaveChangesAsync(); return id;
    }
    private async Task SetProgressAsync(string id, string status, int progress, string title, string summary) { await using var db = await _dbFactory.CreateDbContextAsync(); var x = await db.Incidents.FindAsync(id) ?? throw NotFound(); x.Status = status; x.Progress = progress; db.IncidentTimeline.Add(Event(id, DateTimeOffset.UtcNow, "progress", "info", title, summary)); await db.SaveChangesAsync(); }
    private async Task TimelineAsync(string id, string type, string severity, string title, string summary, string? evidenceId = null) { await using var db = await _dbFactory.CreateDbContextAsync(); db.IncidentTimeline.Add(Event(id, DateTimeOffset.UtcNow, type, severity, title, Normalize(summary, 2000) ?? title, evidenceId)); await db.SaveChangesAsync(); }
    private async Task CompleteAsync(string id, List<string> failures) { await using var db = await _dbFactory.CreateDbContextAsync(); var x = await db.Incidents.FindAsync(id) ?? throw NotFound(); x.Status = failures.Count == 0 ? "ready" : "partial"; x.Progress = 100; x.WindowEnd = DateTimeOffset.UtcNow; x.CompletedAt = x.WindowEnd; x.ErrorMessage = failures.Count == 0 ? null : string.Join(" ", failures).Length > 1200 ? string.Join(" ", failures)[..1200] : string.Join(" ", failures); db.IncidentTimeline.Add(Event(id, x.CompletedAt.Value, "incident", failures.Count == 0 ? "info" : "warning", failures.Count == 0 ? "Tracebag ready" : "Tracebag partially ready", failures.Count == 0 ? "All requested evidence was captured and analyzed." : "Available evidence was preserved; one or more sources were unavailable.")); await db.SaveChangesAsync(); }
    private async Task FailAsync(string id, string message) { await using var db = await _dbFactory.CreateDbContextAsync(); var x = await db.Incidents.FindAsync(id); if (x is null) { return; } x.Status = "failed"; x.CompletedAt = DateTimeOffset.UtcNow; x.WindowEnd = x.CompletedAt; x.ErrorMessage = Normalize(message, 1200); db.IncidentTimeline.Add(Event(id, x.CompletedAt.Value, "incident", "error", "Capture failed", x.ErrorMessage ?? "Unknown error.")); await db.SaveChangesAsync(); }
    private async Task<IncidentRecord> IncidentAsync(string id) { await using var db = await _dbFactory.CreateDbContextAsync(); return await db.Incidents.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id) ?? throw NotFound(); }
    private async Task<int> ProcessIdAsync(string id) => (await IncidentAsync(id)).ProcessId;
    private static IncidentTimelineRecord Event(string id, DateTimeOffset at, string type, string severity, string title, string summary, string? evidenceId = null) => new() { IncidentId = id, Timestamp = at, Type = type, Severity = severity, Title = title, Summary = summary, EvidenceId = evidenceId };
    private static IncidentSummaryDto ToSummary(IncidentRecord x) => new(x.Id, x.ContainerId, x.ContainerName, x.ProcessId, x.Title, x.Profile, x.Reason, x.Notes, x.Status, x.Progress, x.CreatedBy, x.CreatedAt, x.WindowStart, x.WindowEnd, x.CompletedAt, x.ErrorMessage);
    private static IncidentTimelineDto ToTimeline(IncidentTimelineRecord x) => new(x.Id, x.Timestamp, x.Type, x.Severity, x.Title, x.Summary, x.EvidenceId, Parse(x.MetadataJson));
    private static IncidentEvidenceDto ToEvidence(IncidentEvidenceRecord x) => new(x.Id, x.Kind, x.Title, x.CapturedAt, x.From, x.To, x.SourceId, x.ArtifactId, Parse(x.SummaryJson)!, Parse(x.PayloadJson)!, x.SelectedByDefault, x.Sensitive, x.RedactionStatus);
    private static IncidentFindingDto ToFinding(IncidentFindingRecord x, IncidentFindingEvidenceRecord[] links) => new(x.Id, x.Code, x.Severity, x.Confidence, x.Title, x.Summary, x.CreatedAt, links.Where(l => l.FindingId == x.Id).Select(l => l.EvidenceId).ToArray());
    private static object? Parse(string? json) => string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<object>(json, JsonOptions);
    internal static void EnsureCapacity(int incidentCount, int maximum)
    {
        if (incidentCount >= maximum)
        {
            throw new TracebagException(
                StatusCodes.Status409Conflict,
                "incident_capacity_reached",
                "The incident capacity has been reached. Export and delete an existing incident before creating another one.");
        }
    }
    private static string? Normalize(string? value, int max) { value = value?.Trim(); return string.IsNullOrWhiteSpace(value) ? null : value.Length <= max ? value : value[..max]; }
    private static TracebagException NotFound() => new(404, "incident_not_found", "The requested incident was not found.");
}

public sealed class IncidentRecoveryService(IncidentService incidents) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => incidents.RecoverAsync(cancellationToken);
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
