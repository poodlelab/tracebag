using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tracebag.Api.Auth;
using Tracebag.Api.Data;
using Tracebag.Api.Models;
using Tracebag.Api.Security;

namespace Tracebag.Api.Diagnostics;

public sealed class DiagnosticJobStore : IDisposable
{
    public static readonly string[] ActiveStatuses = ["queued", "validating", "starting", "running", "collecting", "stopping"];
    public static readonly string[] TerminalStatuses = ["completed", "cancelled", "failed", "timed_out", "target_exited"];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IDbContextFactory<TracebagDbContext> _dbContextFactory;
    private readonly TracebagOptions _options;
    private readonly SemaphoreSlim _reservationLock = new(1, 1);

    public DiagnosticJobStore(IDbContextFactory<TracebagDbContext> dbContextFactory, TracebagOptions options)
    {
        _dbContextFactory = dbContextFactory;
        _options = options;
    }

    public async Task<DiagnosticJobResponse> ReserveAsync(DiagnosticJobReservation reservation, CancellationToken cancellationToken)
    {
        await _reservationLock.WaitAsync(cancellationToken);
        try
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            await using var transaction = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
                : null;
            if (db.Database.IsNpgsql())
            {
                await db.Database.ExecuteSqlRawAsync("LOCK TABLE diagnostic_jobs IN SHARE ROW EXCLUSIVE MODE", cancellationToken);
            }

            if (!string.IsNullOrWhiteSpace(reservation.IdempotencyKey))
            {
                var existing = await db.DiagnosticJobs.AsNoTracking()
                    .FirstOrDefaultAsync(job => job.IdempotencyKey == reservation.IdempotencyKey, cancellationToken);
                if (existing is not null)
                {
                    if (!string.Equals(existing.RequestFingerprint, reservation.RequestFingerprint, StringComparison.Ordinal))
                    {
                        throw new TracebagException(StatusCodes.Status409Conflict, "idempotency_key_reused", "The idempotency key was already used for a different diagnostic request.");
                    }

                    return ToDto(existing);
                }
            }

            if (await db.DiagnosticJobs.AnyAsync(job => job.ContainerId == reservation.ContainerId && ActiveStatuses.Contains(job.Status), cancellationToken))
            {
                throw new TracebagException(StatusCodes.Status409Conflict, "diagnostic_target_busy", "This target already has an active diagnostic capture.");
            }

            if (await db.DiagnosticJobs.CountAsync(job => ActiveStatuses.Contains(job.Status), cancellationToken) >= _options.DiagnosticJobMaxActiveGlobal)
            {
                throw new TracebagException(StatusCodes.Status429TooManyRequests, "diagnostic_global_limit", "The global diagnostic capture limit has been reached.");
            }

            var today = DateTimeOffset.UtcNow.Date;
            if (await db.DiagnosticJobs.CountAsync(job => job.CreatedAt >= today, cancellationToken) >= _options.DiagnosticJobDailyLimit)
            {
                throw new TracebagException(StatusCodes.Status429TooManyRequests, "diagnostic_daily_limit", "The daily diagnostic capture limit has been reached.");
            }

            var record = new DiagnosticJobRecord
            {
                Id = reservation.Id,
                ContainerId = reservation.ContainerId,
                ContainerName = reservation.ContainerName,
                DockerId = reservation.DockerId,
                ProcessId = reservation.ProcessId,
                Profile = reservation.Profile,
                Status = "queued",
                Progress = 0,
                StatusMessage = "Capture queued.",
                CreatedAt = reservation.CreatedAt,
                DeadlineAt = reservation.DeadlineAt,
                CreatedBy = string.IsNullOrWhiteSpace(reservation.CreatedBy) ? "anonymous" : reservation.CreatedBy,
                IdempotencyKey = Normalize(reservation.IdempotencyKey, 160),
                RequestFingerprint = reservation.RequestFingerprint,
                InputsJson = reservation.InputsJson,
                RuntimeMajor = reservation.Runner.RuntimeMajor,
                RunnerImage = reservation.Runner.Image,
                ToolVersion = reservation.Runner.ToolVersion
            };
            db.DiagnosticJobs.Add(record);
            db.DiagnosticJobEvents.Add(Event(record, "state", "Capture queued."));
            await db.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return ToDto(record);
        }
        finally
        {
            _reservationLock.Release();
        }
    }

    public async Task<IReadOnlyList<DiagnosticJobResponse>> ListAsync(string? containerId, string? status, CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var query = db.DiagnosticJobs.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(containerId))
        {
            query = query.Where(job => job.ContainerId == containerId);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = string.Equals(status, "active", StringComparison.OrdinalIgnoreCase)
                ? query.Where(job => ActiveStatuses.Contains(job.Status))
                : query.Where(job => job.Status == status);
        }

        return (await query.OrderByDescending(job => job.CreatedAt).Take(100).ToListAsync(cancellationToken)).Select(ToDto).ToArray();
    }

    public async Task<DiagnosticJobResponse> GetAsync(string jobId, CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var record = await db.DiagnosticJobs.AsNoTracking().FirstOrDefaultAsync(job => job.Id == jobId, cancellationToken)
            ?? throw NotFound();
        return ToDto(record);
    }

    public async Task<IReadOnlyList<DiagnosticJobEventResponse>> GetEventsAsync(string jobId, long afterId, CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (!await db.DiagnosticJobs.AnyAsync(job => job.Id == jobId, cancellationToken))
        {
            throw NotFound();
        }

        var events = await db.DiagnosticJobEvents.AsNoTracking()
            .Where(item => item.JobId == jobId && item.Id > afterId)
            .OrderBy(item => item.Id)
            .Take(200)
            .ToListAsync(cancellationToken);
        return events.Select(ToEventDto).ToArray();
    }

    public async Task TransitionAsync(
        string jobId,
        string status,
        int progress,
        string message,
        string? runnerContainerId = null,
        string? artifactId = null,
        string? outcomeJson = null,
        string? errorCode = null,
        string? errorMessage = null,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var record = await db.DiagnosticJobs.FirstOrDefaultAsync(job => job.Id == jobId, cancellationToken) ?? throw NotFound();
        if (TerminalStatuses.Contains(record.Status))
        {
            return;
        }

        EnsureTransition(record.Status, status);
        var now = DateTimeOffset.UtcNow;
        record.Status = status;
        record.Progress = Math.Clamp(progress, 0, 100);
        record.StatusMessage = Normalize(message, 600);
        if (status == "running" && record.StartedAt is null)
        {
            record.StartedAt = now;
        }

        if (runnerContainerId is not null)
        {
            record.RunnerContainerId = runnerContainerId;
        }

        if (artifactId is not null)
        {
            record.ArtifactId = artifactId;
        }

        if (outcomeJson is not null)
        {
            record.OutcomeJson = outcomeJson;
        }

        if (errorCode is not null)
        {
            record.ErrorCode = Normalize(errorCode, 80);
        }

        if (errorMessage is not null)
        {
            record.ErrorMessage = Normalize(errorMessage, 1200);
        }

        if (TerminalStatuses.Contains(status))
        {
            record.CompletedAt = now;
            record.RunnerContainerId = null;
        }

        db.DiagnosticJobEvents.Add(Event(record, TerminalStatuses.Contains(status) ? "completed" : "state", message));
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<DiagnosticJobResponse> RequestCancellationAsync(string jobId, CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var record = await db.DiagnosticJobs.FirstOrDefaultAsync(job => job.Id == jobId, cancellationToken) ?? throw NotFound();
        if (TerminalStatuses.Contains(record.Status))
        {
            return ToDto(record);
        }

        record.CancelRequestedAt ??= DateTimeOffset.UtcNow;
        if (record.Status != "stopping")
        {
            record.Status = "stopping";
            record.StatusMessage = "Cancellation requested; cleaning up the runner.";
            db.DiagnosticJobEvents.Add(Event(record, "state", record.StatusMessage));
        }
        await db.SaveChangesAsync(cancellationToken);

        return ToDto(record);
    }

    public async Task<IReadOnlyList<DiagnosticJobResponse>> MarkActiveInterruptedAsync(CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var jobs = await db.DiagnosticJobs.Where(job => ActiveStatuses.Contains(job.Status)).ToListAsync(cancellationToken);
        foreach (var job in jobs)
        {
            job.Status = "failed";
            job.Progress = Math.Min(job.Progress, 99);
            job.CompletedAt = DateTimeOffset.UtcNow;
            job.ErrorCode = "tracebag_restarted";
            job.ErrorMessage = "Tracebag restarted while the capture was active; its runner was reconciled.";
            job.StatusMessage = job.ErrorMessage;
            job.RunnerContainerId = null;
            db.DiagnosticJobEvents.Add(Event(job, "completed", job.ErrorMessage));
        }

        await db.SaveChangesAsync(cancellationToken);
        return jobs.Select(ToDto).ToArray();
    }

    public static bool IsTerminal(string status) => TerminalStatuses.Contains(status);

    private static void EnsureTransition(string from, string to)
    {
        if (from == to)
        {
            return;
        }

        var valid = from switch
        {
            "queued" => to is "validating" or "stopping" or "failed",
            "validating" => to is "starting" or "stopping" or "failed",
            "starting" => to is "running" or "stopping" or "failed" or "timed_out" or "target_exited",
            "running" => to is "collecting" or "stopping" or "failed" or "timed_out" or "target_exited",
            "collecting" => to is "completed" or "stopping" or "failed" or "timed_out" or "target_exited",
            "stopping" => to is "completed" or "cancelled" or "failed" or "timed_out" or "target_exited",
            _ => false
        };
        if (!valid)
        {
            throw new InvalidOperationException($"Invalid diagnostic job transition from '{from}' to '{to}'.");
        }
    }

    private static DiagnosticJobEventRecord Event(DiagnosticJobRecord job, string type, string message) => new()
    {
        JobId = job.Id,
        Timestamp = DateTimeOffset.UtcNow,
        Type = type,
        Status = job.Status,
        Progress = job.Progress,
        Message = message
    };

    private static DiagnosticJobResponse ToDto(DiagnosticJobRecord job) => new(
        job.Id, job.ContainerId, job.ContainerName, job.Profile, job.Status, job.Progress, job.StatusMessage,
        job.CreatedAt, job.StartedAt, job.CompletedAt, job.DeadlineAt, job.CreatedBy, job.ProcessId,
        job.RuntimeMajor, job.RunnerImage, job.ToolVersion, job.ArtifactId,
        JsonSerializer.Deserialize<object>(job.InputsJson, JsonOptions) ?? new { },
        string.IsNullOrWhiteSpace(job.OutcomeJson) ? null : JsonSerializer.Deserialize<object>(job.OutcomeJson, JsonOptions),
        job.ErrorCode, job.ErrorMessage);

    private static DiagnosticJobEventResponse ToEventDto(DiagnosticJobEventRecord item)
    {
        object? metadata = null;
        if (!string.IsNullOrWhiteSpace(item.MetadataJson))
        {
            metadata = JsonSerializer.Deserialize<object>(item.MetadataJson, JsonOptions);
        }

        return new(item.Id, item.JobId, item.Timestamp, item.Type, item.Status, item.Progress, item.Message, metadata);
    }

    private static TracebagException NotFound() => new(StatusCodes.Status404NotFound, "diagnostic_job_not_found", "The diagnostic job was not found.");
    private static string? Normalize(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, max)];

    public void Dispose() => _reservationLock.Dispose();
}

public sealed record DiagnosticJobReservation(
    string Id,
    string ContainerId,
    string ContainerName,
    string DockerId,
    int ProcessId,
    string Profile,
    DateTimeOffset CreatedAt,
    DateTimeOffset DeadlineAt,
    string CreatedBy,
    string? IdempotencyKey,
    string RequestFingerprint,
    string InputsJson,
    DiagnosticRunnerSelection Runner);
