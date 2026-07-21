using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tracebag.Api.Auth;
using Tracebag.Api.Data;
using Tracebag.Api.Models;
using Tracebag.Api.Security;

namespace Tracebag.Api.Diagnostics;

public sealed class CounterRecordingStore : IDisposable
{
    private const int SampleReadLimit = 50_000;
    private const int RollupReadLimit = 250_000;
    private const int ExportReadLimit = 1_000_000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] ActiveStatuses = ["starting", "running", "stopping"];
    private readonly SemaphoreSlim _reservationLock = new(1, 1);
    private readonly IDbContextFactory<TracebagDbContext>? _dbContextFactory;
    private readonly TracebagOptions _options;

    public CounterRecordingStore(TracebagOptions options, IDbContextFactory<TracebagDbContext>? dbContextFactory = null)
    {
        _options = options;
        _dbContextFactory = dbContextFactory;
    }

    public async Task<IReadOnlyList<CounterRecordingResponse>> ListAsync(
        string? status,
        string? containerId,
        CancellationToken cancellationToken)
    {
        await using var db = await CreateDbContextAsync(cancellationToken);
        var query = db.CounterRecordingSessions.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = string.Equals(status, "active", StringComparison.OrdinalIgnoreCase)
                ? query.Where(recording => ActiveStatuses.Contains(recording.Status))
                : query.Where(recording => recording.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(containerId))
        {
            query = query.Where(recording => recording.ContainerId == containerId);
        }

        var recordings = await query
            .OrderByDescending(recording => recording.StartedAt)
            .Take(100)
            .ToListAsync(cancellationToken);
        return recordings.Select(ToDto).ToArray();
    }

    public async Task<CounterRecordingResponse> GetAsync(string recordingId, CancellationToken cancellationToken)
    {
        await using var db = await CreateDbContextAsync(cancellationToken);
        var record = await db.CounterRecordingSessions.AsNoTracking()
            .FirstOrDefaultAsync(recording => recording.Id == recordingId, cancellationToken)
            ?? throw NotFound();
        return ToDto(record);
    }

    public async Task<CounterRecordingDetailResponse> GetDetailAsync(string recordingId, CancellationToken cancellationToken)
    {
        var recording = await GetAsync(recordingId, cancellationToken);
        var series = await GetSeriesDescriptorsAsync(recordingId, cancellationToken);
        return new CounterRecordingDetailResponse(recording, series);
    }

    public async Task ReserveAsync(
        string recordingId,
        string containerId,
        string containerName,
        int processId,
        string preset,
        IReadOnlyList<string> providers,
        int intervalSeconds,
        int maxDurationSeconds,
        string? name,
        string createdBy,
        DiagnosticRunnerSelection runner,
        CancellationToken cancellationToken)
    {
        await _reservationLock.WaitAsync(cancellationToken);
        try
        {
            await using var db = await CreateDbContextAsync(cancellationToken);
            await using var transaction = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
                : null;

            if (db.Database.IsNpgsql())
            {
                await db.Database.ExecuteSqlRawAsync(
                    "LOCK TABLE counter_recording_sessions IN SHARE ROW EXCLUSIVE MODE",
                    cancellationToken);
            }

            if (await db.CounterRecordingSessions.AnyAsync(
                    recording => recording.ContainerId == containerId && ActiveStatuses.Contains(recording.Status),
                    cancellationToken))
            {
                throw new TracebagException(
                    StatusCodes.Status409Conflict,
                    "counter_recording_already_running",
                    "There is already an active counter recording for this container.");
            }

            if (await db.CounterRecordingSessions.CountAsync(
                    recording => ActiveStatuses.Contains(recording.Status),
                    cancellationToken) >= _options.CounterRecordingMaxActiveGlobal)
            {
                throw new TracebagException(
                    StatusCodes.Status409Conflict,
                    "counter_recording_global_limit",
                    "The global active counter recording limit has been reached.");
            }

            db.CounterRecordingSessions.Add(new CounterRecordingSessionRecord
            {
                Id = recordingId,
                ContainerId = containerId,
                ContainerName = containerName,
                Name = Normalize(name, 160),
                ProcessId = processId,
                Preset = preset,
                IntervalSeconds = intervalSeconds,
                MaxDurationSeconds = maxDurationSeconds,
                StartedAt = DateTimeOffset.UtcNow,
                Status = "starting",
                CreatedBy = string.IsNullOrWhiteSpace(createdBy) ? "anonymous" : createdBy,
                ProvidersJson = JsonSerializer.Serialize(providers, JsonOptions),
                RuntimeMajor = runner.RuntimeMajor,
                RunnerImage = runner.Image,
                ToolVersion = runner.ToolVersion
            });
            await db.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        finally
        {
            _reservationLock.Release();
        }
    }

    public async Task MarkRunningAsync(string recordingId, string runnerContainerId, CancellationToken cancellationToken)
    {
        await using var db = await CreateDbContextAsync(cancellationToken);
        var record = await FindMutableAsync(db, recordingId, cancellationToken);
        if (!string.Equals(record.Status, "starting", StringComparison.Ordinal))
        {
            throw InvalidTransition(record.Status, "running");
        }

        record.RunnerContainerId = runnerContainerId;
        record.Status = "running";
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> MarkStoppingAsync(string recordingId, CancellationToken cancellationToken)
    {
        await using var db = await CreateDbContextAsync(cancellationToken);
        var record = await FindMutableAsync(db, recordingId, cancellationToken);
        if (!ActiveStatuses.Contains(record.Status))
        {
            return false;
        }

        if (!string.Equals(record.Status, "stopping", StringComparison.Ordinal))
        {
            record.Status = "stopping";
            await db.SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    public async Task MarkFinishedAsync(
        string recordingId,
        string status,
        string stopReason,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        if (status is not ("completed" or "stopped" or "failed" or "timed_out" or "cancelled"))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported terminal recording status.");
        }

        await using var db = await CreateDbContextAsync(cancellationToken);
        var record = await FindMutableAsync(db, recordingId, cancellationToken);
        if (!ActiveStatuses.Contains(record.Status))
        {
            return;
        }

        record.Status = status;
        record.StopReason = stopReason;
        record.ErrorMessage = Normalize(errorMessage, 600);
        record.StoppedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<CounterRecordingResponse> UpdateMetadataAsync(
        string recordingId,
        string? name,
        string? notes,
        CancellationToken cancellationToken)
    {
        await using var db = await CreateDbContextAsync(cancellationToken);
        var record = await FindMutableAsync(db, recordingId, cancellationToken);
        record.Name = Normalize(name, 160);
        record.Notes = Normalize(notes, 4_000);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(record);
    }

    public async Task<IReadOnlyList<CounterRecordingResponse>> MarkActiveInterruptedAsync(CancellationToken cancellationToken)
    {
        await using var db = await CreateDbContextAsync(cancellationToken);
        var records = await db.CounterRecordingSessions
            .Where(recording => ActiveStatuses.Contains(recording.Status))
            .ToListAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        foreach (var record in records)
        {
            record.Status = "failed";
            record.StopReason = "interrupted";
            record.ErrorMessage = "Tracebag restarted while the recording was running; the orphan runner was reconciled.";
            record.StoppedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        return records.Select(ToDto).ToArray();
    }

    public async Task AddSamplesAsync(string recordingId, IReadOnlyList<CounterSample> samples, CancellationToken cancellationToken)
    {
        if (samples.Count == 0)
        {
            return;
        }

        await using var db = await CreateDbContextAsync(cancellationToken);
        var record = await FindMutableAsync(db, recordingId, cancellationToken);
        var normalized = samples.Select(sample => sample with { CapturedAt = sample.CapturedAt.ToUniversalTime() }).ToArray();
        db.CounterSamples.AddRange(normalized.Select(sample => new CounterSampleRecord
        {
            SessionId = recordingId,
            CapturedAt = sample.CapturedAt,
            Provider = sample.Provider,
            Name = sample.Name,
            CounterType = sample.CounterType,
            Value = sample.Value
        }));

        var firstBucket = ToMinute(normalized.Min(sample => sample.CapturedAt));
        var lastBucket = ToMinute(normalized.Max(sample => sample.CapturedAt));
        var existing = await db.CounterRollups1m
            .Where(rollup => rollup.SessionId == recordingId
                && rollup.BucketStart >= firstBucket
                && rollup.BucketStart <= lastBucket)
            .ToListAsync(cancellationToken);
        var byKey = existing.ToDictionary(RollupKey, StringComparer.Ordinal);

        foreach (var group in normalized.GroupBy(sample => new
                 {
                     sample.Provider,
                     sample.Name,
                     sample.CounterType,
                     Bucket = ToMinute(sample.CapturedAt)
                 }))
        {
            var key = RollupKey(group.Key.Provider, group.Key.Name, group.Key.CounterType, group.Key.Bucket);
            var count = group.Count();
            var sum = group.Sum(sample => sample.Value);
            if (byKey.TryGetValue(key, out var rollup))
            {
                var total = checked(rollup.Count + count);
                rollup.Average = ((rollup.Average * rollup.Count) + sum) / total;
                rollup.Minimum = Math.Min(rollup.Minimum, group.Min(sample => sample.Value));
                rollup.Maximum = Math.Max(rollup.Maximum, group.Max(sample => sample.Value));
                rollup.Count = total;
            }
            else
            {
                rollup = new CounterRollup1mRecord
                {
                    SessionId = recordingId,
                    ContainerId = record.ContainerId,
                    Provider = group.Key.Provider,
                    Name = group.Key.Name,
                    CounterType = group.Key.CounterType,
                    BucketStart = group.Key.Bucket,
                    Average = sum / count,
                    Minimum = group.Min(sample => sample.Value),
                    Maximum = group.Max(sample => sample.Value),
                    Count = count
                };
                db.CounterRollups1m.Add(rollup);
                byKey[key] = rollup;
            }
        }

        record.SampleCount += normalized.Length;
        record.LastSampleAt = normalized.Max(sample => sample.CapturedAt);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<CounterRecordingSamplesResponse> GetSamplesAsync(
        string recordingId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? resolution,
        CancellationToken cancellationToken)
    {
        var recording = await GetAsync(recordingId, cancellationToken);
        var normalizedResolution = SelectResolution(recording, from, to, resolution);
        await using var db = await CreateDbContextAsync(cancellationToken);
        List<SeriesPoint> points;

        if (normalizedResolution == "1m")
        {
            var query = db.CounterRollups1m.AsNoTracking().Where(rollup => rollup.SessionId == recordingId);
            if (from.HasValue)
            {
                query = query.Where(rollup => rollup.BucketStart >= from.Value);
            }

            if (to.HasValue)
            {
                query = query.Where(rollup => rollup.BucketStart <= to.Value);
            }

            points = await query.OrderBy(rollup => rollup.BucketStart)
                .Take(RollupReadLimit + 1)
                .Select(rollup => new SeriesPoint(
                    rollup.Provider,
                    rollup.Name,
                    rollup.CounterType,
                    rollup.BucketStart,
                    rollup.Average,
                    rollup.Minimum,
                    rollup.Maximum,
                    rollup.Count))
                .ToListAsync(cancellationToken);
        }
        else
        {
            var query = db.CounterSamples.AsNoTracking().Where(sample => sample.SessionId == recordingId);
            if (from.HasValue)
            {
                query = query.Where(sample => sample.CapturedAt >= from.Value);
            }

            if (to.HasValue)
            {
                query = query.Where(sample => sample.CapturedAt <= to.Value);
            }

            points = await query.OrderBy(sample => sample.CapturedAt)
                .Take(SampleReadLimit + 1)
                .Select(sample => new SeriesPoint(
                    sample.Provider,
                    sample.Name,
                    sample.CounterType,
                    sample.CapturedAt,
                    sample.Value,
                    sample.Value,
                    sample.Value,
                    1))
                .ToListAsync(cancellationToken);
        }

        var readLimit = normalizedResolution == "1m" ? RollupReadLimit : SampleReadLimit;
        var truncated = points.Count > readLimit;
        if (truncated)
        {
            points.RemoveAt(points.Count - 1);
        }

        var series = points
            .GroupBy(point => new { point.Provider, point.Name, point.CounterType })
            .Select(group => ToSeries(group.Key.Provider, group.Key.Name, group.Key.CounterType, group))
            .OrderBy(item => item.Provider)
            .ThenBy(item => item.Name)
            .ToArray();

        return new CounterRecordingSamplesResponse(recordingId, normalizedResolution, ["raw", "1m"], truncated, series);
    }

    public async Task<CounterRecordingExport> ExportAsync(
        string recordingId,
        string? format,
        CancellationToken cancellationToken)
    {
        var recording = await GetAsync(recordingId, cancellationToken);
        await using var db = await CreateDbContextAsync(cancellationToken);
        var samples = await db.CounterSamples.AsNoTracking()
            .Where(sample => sample.SessionId == recordingId)
            .OrderBy(sample => sample.CapturedAt)
            .Take(ExportReadLimit + 1)
            .ToListAsync(cancellationToken);
        if (samples.Count > ExportReadLimit)
        {
            throw new TracebagException(
                StatusCodes.Status413PayloadTooLarge,
                "recording_export_too_large",
                $"Exports are limited to {ExportReadLimit.ToString("N0", CultureInfo.InvariantCulture)} raw samples.");
        }

        var safeName = string.Join('-', (recording.Name ?? recording.Id).Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrWhiteSpace(safeName))
        {
            safeName = recording.Id;
        }
        if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
        {
            var content = JsonSerializer.SerializeToUtf8Bytes(new { recording, samples }, JsonOptions);
            return new CounterRecordingExport("application/json", $"{safeName}.json", content);
        }

        if (!string.IsNullOrWhiteSpace(format) && !string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
        {
            throw new TracebagException(StatusCodes.Status400BadRequest, "recording_export_format_invalid", "Export format must be csv or json.");
        }

        var csv = new StringBuilder("timestamp,provider,name,counterType,value\n");
        foreach (var sample in samples)
        {
            csv.Append(Csv(sample.CapturedAt.ToString("O", CultureInfo.InvariantCulture))).Append(',')
                .Append(Csv(sample.Provider)).Append(',')
                .Append(Csv(sample.Name)).Append(',')
                .Append(Csv(sample.CounterType)).Append(',')
                .Append(sample.Value.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
        }

        return new CounterRecordingExport("text/csv; charset=utf-8", $"{safeName}.csv", Encoding.UTF8.GetBytes(csv.ToString()));
    }

    public async Task DeleteAsync(string recordingId, CancellationToken cancellationToken)
    {
        await using var db = await CreateDbContextAsync(cancellationToken);
        var record = await db.CounterRecordingSessions
            .FirstOrDefaultAsync(recording => recording.Id == recordingId, cancellationToken)
            ?? throw NotFound();
        db.CounterRecordingSessions.Remove(record);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> IsReferencedByIncidentAsync(string recordingId, CancellationToken cancellationToken)
    {
        await using var db = await CreateDbContextAsync(cancellationToken);
        return await db.IncidentEvidence.AnyAsync(
            evidence => evidence.Kind == "counter-window" && evidence.SourceId == recordingId,
            cancellationToken);
    }

    public async Task ApplyRetentionAsync(CancellationToken cancellationToken)
    {
        await using var db = await CreateDbContextAsync(cancellationToken);
        var cutoff = DateTimeOffset.UtcNow.AddDays(-_options.CounterRecordingRetentionDays);
        var expired = await db.CounterRecordingSessions
            .Where(recording => recording.StartedAt < cutoff
                && !ActiveStatuses.Contains(recording.Status)
                && !db.IncidentEvidence.Any(evidence =>
                    evidence.Kind == "counter-window" && evidence.SourceId == recording.Id))
            .OrderBy(recording => recording.StartedAt)
            .Take(100)
            .ToListAsync(cancellationToken);
        if (expired.Count > 0)
        {
            db.CounterRecordingSessions.RemoveRange(expired);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<IReadOnlyList<CounterSeriesDescriptorDto>> GetSeriesDescriptorsAsync(string recordingId, CancellationToken cancellationToken)
    {
        await using var db = await CreateDbContextAsync(cancellationToken);
        var descriptors = await db.CounterSamples.AsNoTracking()
            .Where(sample => sample.SessionId == recordingId)
            .GroupBy(sample => new { sample.Provider, sample.Name, sample.CounterType })
            .Select(group => new CounterSeriesDescriptorDto(
                group.Key.Provider,
                group.Key.Name,
                group.Key.CounterType,
                group.LongCount(),
                group.Min(sample => (DateTimeOffset?)sample.CapturedAt),
                group.Max(sample => (DateTimeOffset?)sample.CapturedAt)))
            .ToListAsync(cancellationToken);
        return descriptors.OrderBy(series => series.Provider).ThenBy(series => series.Name).ToArray();
    }

    private async Task<TracebagDbContext> CreateDbContextAsync(CancellationToken cancellationToken)
    {
        if (_dbContextFactory is null)
        {
            throw new TracebagException(
                StatusCodes.Status503ServiceUnavailable,
                "recording_database_required",
                "Counter recording requires the Tracebag PostgreSQL database.");
        }

        return await _dbContextFactory.CreateDbContextAsync(cancellationToken);
    }

    private static async Task<CounterRecordingSessionRecord> FindMutableAsync(
        TracebagDbContext db,
        string recordingId,
        CancellationToken cancellationToken)
    {
        return await db.CounterRecordingSessions
            .FirstOrDefaultAsync(recording => recording.Id == recordingId, cancellationToken)
            ?? throw NotFound();
    }

    private static CounterRecordingResponse ToDto(CounterRecordingSessionRecord record)
    {
        return new CounterRecordingResponse(
            record.Id,
            record.ContainerId,
            record.ContainerName,
            record.ProcessId,
            record.Preset,
            record.IntervalSeconds,
            record.StartedAt,
            record.StoppedAt,
            record.LastSampleAt,
            record.Status,
            record.SampleCount,
            record.CreatedBy,
            record.Name,
            record.StopReason,
            record.ErrorMessage,
            record.Notes,
            record.RuntimeMajor,
            record.RunnerImage,
            record.ToolVersion);
    }

    private static CounterSeriesDto ToSeries(
        string provider,
        string name,
        string counterType,
        IEnumerable<SeriesPoint> source)
    {
        var points = source.OrderBy(point => point.Timestamp).ToArray();
        var count = points.Sum(point => (long)point.Count);
        var peak = points.MaxBy(point => point.Maximum)!;
        var summary = new CounterSeriesSummaryDto(
            points.Min(point => point.Minimum),
            points.Max(point => point.Maximum),
            points.Sum(point => point.Value * point.Count) / Math.Max(1, count),
            peak.Timestamp,
            count);
        return new CounterSeriesDto(
            provider,
            name,
            counterType,
            summary,
            points.Select(point => new CounterSamplePointDto(
                point.Timestamp,
                point.Value,
                point.Minimum,
                point.Maximum,
                point.Count)).ToArray());
    }

    private static string SelectResolution(
        CounterRecordingResponse recording,
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? resolution)
    {
        if (!string.IsNullOrWhiteSpace(resolution)
            && !string.Equals(resolution, "auto", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(resolution, "raw", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(resolution, "1m", StringComparison.OrdinalIgnoreCase))
        {
            throw new TracebagException(StatusCodes.Status400BadRequest, "recording_resolution_invalid", "Resolution must be auto, raw, or 1m.");
        }

        if (string.Equals(resolution, "raw", StringComparison.OrdinalIgnoreCase))
        {
            return "raw";
        }

        if (string.Equals(resolution, "1m", StringComparison.OrdinalIgnoreCase))
        {
            return "1m";
        }

        var recordingEnd = recording.StoppedAt ?? DateTimeOffset.UtcNow;
        var totalRange = recordingEnd - recording.StartedAt;
        var range = (to ?? recordingEnd) - (from ?? recording.StartedAt);
        var estimatedSamples = totalRange.TotalSeconds <= 0
            ? recording.SampleCount
            : recording.SampleCount * Math.Clamp(range.TotalSeconds / totalRange.TotalSeconds, 0, 1);
        return range > TimeSpan.FromHours(2) || estimatedSamples > SampleReadLimit ? "1m" : "raw";
    }

    private static DateTimeOffset ToMinute(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, 0, TimeSpan.Zero);
    }

    private static string RollupKey(CounterRollup1mRecord rollup) =>
        RollupKey(rollup.Provider, rollup.Name, rollup.CounterType, rollup.BucketStart);

    private static string RollupKey(string provider, string name, string counterType, DateTimeOffset bucket) =>
        $"{provider}\u001f{name}\u001f{counterType}\u001f{bucket.UtcTicks}";

    private static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";

    private static string? Normalize(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private static TracebagException NotFound() =>
        new(StatusCodes.Status404NotFound, "recording_not_found", "The requested counter recording was not found.");

    private static TracebagException InvalidTransition(string current, string requested) =>
        new(StatusCodes.Status409Conflict, "recording_transition_invalid", $"Recording cannot transition from {current} to {requested}.");

    public void Dispose() => _reservationLock.Dispose();

    private sealed record SeriesPoint(
        string Provider,
        string Name,
        string CounterType,
        DateTimeOffset Timestamp,
        double Value,
        double Minimum,
        double Maximum,
        int Count);
}

public sealed record CounterRecordingExport(string ContentType, string FileName, byte[] Content);
