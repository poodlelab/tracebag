using Microsoft.EntityFrameworkCore;
using Tracebag.Api.Auth;
using Tracebag.Api.Data;
using Tracebag.Api.Diagnostics;

namespace Tracebag.Api.Retention;

public sealed class DurableRetentionStore(
    IDbContextFactory<TracebagDbContext> dbContextFactory,
    TracebagOptions options)
{
    private const int MaximumBatchesPerPass = 10;
    private readonly object _stateLock = new();
    private DateTimeOffset? _lastCompletedAt;
    private int _lastDeletedJobs;
    private string? _lastError;

    public async Task<DurableRetentionResult> ApplyAsync(CancellationToken cancellationToken)
    {
        var deletedJobs = 0;
        var cutoff = DateTimeOffset.UtcNow.AddDays(-options.DiagnosticJobRetentionDays);
        for (var batch = 0; batch < MaximumBatchesPerPass; batch++)
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var referencedJobIds = db.IncidentEvidence
                .Where(evidence => evidence.Kind == "diagnostic-artifact" && evidence.SourceId != null)
                .Select(evidence => evidence.SourceId!);
            var ids = await db.DiagnosticJobs
                .Where(job => DiagnosticJobStore.TerminalStatuses.Contains(job.Status)
                    && job.CreatedAt < cutoff
                    && !referencedJobIds.Contains(job.Id))
                .OrderBy(job => job.CreatedAt)
                .ThenBy(job => job.Id)
                .Select(job => job.Id)
                .Take(options.DiagnosticJobRetentionDeleteBatchSize)
                .ToArrayAsync(cancellationToken);
            if (ids.Length == 0)
            {
                break;
            }

            if (db.Database.IsRelational())
            {
                await db.DiagnosticJobs
                    .Where(job => ids.Contains(job.Id))
                    .ExecuteDeleteAsync(cancellationToken);
            }
            else
            {
                var events = await db.DiagnosticJobEvents
                    .Where(jobEvent => ids.Contains(jobEvent.JobId))
                    .ToArrayAsync(cancellationToken);
                var jobs = await db.DiagnosticJobs
                    .Where(job => ids.Contains(job.Id))
                    .ToArrayAsync(cancellationToken);
                db.DiagnosticJobEvents.RemoveRange(events);
                db.DiagnosticJobs.RemoveRange(jobs);
                await db.SaveChangesAsync(cancellationToken);
            }

            deletedJobs += ids.Length;
            if (ids.Length < options.DiagnosticJobRetentionDeleteBatchSize)
            {
                break;
            }
        }

        lock (_stateLock)
        {
            _lastCompletedAt = DateTimeOffset.UtcNow;
            _lastDeletedJobs = deletedJobs;
            _lastError = null;
        }

        return new DurableRetentionResult(deletedJobs);
    }

    public void RecordFailure(Exception exception)
    {
        lock (_stateLock)
        {
            _lastError = SafeMessage(exception);
        }
    }

    public async Task<DurableRetentionSnapshot> StatusAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var cutoff = DateTimeOffset.UtcNow.AddDays(-options.DiagnosticJobRetentionDays);
        var referencedJobIds = db.IncidentEvidence
            .Where(evidence => evidence.Kind == "diagnostic-artifact" && evidence.SourceId != null)
            .Select(evidence => evidence.SourceId!);
        var expiredJobs = db.DiagnosticJobs.Where(job =>
            DiagnosticJobStore.TerminalStatuses.Contains(job.Status) && job.CreatedAt < cutoff);

        var diagnosticJobs = await db.DiagnosticJobs.LongCountAsync(cancellationToken);
        var expiredJobsEligible = await expiredJobs
            .LongCountAsync(job => !referencedJobIds.Contains(job.Id), cancellationToken);
        var expiredJobsProtected = await expiredJobs
            .LongCountAsync(job => referencedJobIds.Contains(job.Id), cancellationToken);
        var incidents = await db.Incidents.LongCountAsync(cancellationToken);
        var activeIncidents = await db.Incidents.LongCountAsync(
            incident => incident.Status == "queued" || incident.Status == "collecting" || incident.Status == "analyzing",
            cancellationToken);
        var incidentArtifacts = await db.IncidentEvidence.LongCountAsync(
            evidence => evidence.ArtifactId != null,
            cancellationToken);
        var incidentRecordings = await db.IncidentEvidence.LongCountAsync(
            evidence => evidence.Kind == "counter-window" && evidence.SourceId != null,
            cancellationToken);
        var auditEvents = await db.AuditEvents.LongCountAsync(cancellationToken);

        DateTimeOffset? lastCompletedAt;
        int lastDeletedJobs;
        string? lastError;
        lock (_stateLock)
        {
            lastCompletedAt = _lastCompletedAt;
            lastDeletedJobs = _lastDeletedJobs;
            lastError = _lastError;
        }

        return new DurableRetentionSnapshot(
            diagnosticJobs,
            expiredJobsEligible,
            expiredJobsProtected,
            incidents,
            activeIncidents,
            options.IncidentMaxCount,
            incidentArtifacts,
            incidentRecordings,
            auditEvents,
            options.AuditMaxEvents,
            lastCompletedAt,
            lastDeletedJobs,
            lastError);
    }

    private static string SafeMessage(Exception exception)
    {
        var message = exception.Message.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return message.Length <= 600 ? message : message[..600];
    }
}

public sealed record DurableRetentionResult(int DeletedJobs);

public sealed record DurableRetentionSnapshot(
    long DiagnosticJobs,
    long ExpiredJobsEligible,
    long ExpiredJobsProtectedByIncidents,
    long Incidents,
    long ActiveIncidents,
    int IncidentMaxCount,
    long IncidentArtifactReferences,
    long IncidentRecordingReferences,
    long AuditEvents,
    int AuditMaxEvents,
    DateTimeOffset? LastCompletedAt,
    int LastDeletedJobs,
    string? LastError);
