using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tracebag.Api.Auth;
using Tracebag.Api.Data;
using Tracebag.Api.Models;
using Tracebag.Api.Security;

namespace Tracebag.Api.Logs;

public sealed class LogStore(
    TracebagOptions options,
    IDbContextFactory<TracebagDbContext>? dbContextFactory = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<DateTimeOffset?> GetResumeTimestampAsync(
        string containerId,
        string dockerId,
        CancellationToken cancellationToken)
    {
        await using var db = await CreateDbContextAsync(cancellationToken);
        var checkpoint = await db.LogCheckpoints.AsNoTracking()
            .SingleOrDefaultAsync(entry => entry.ContainerId == containerId, cancellationToken);
        return checkpoint is not null && checkpoint.DockerId == dockerId && checkpoint.LastTimestamp is not null
            ? checkpoint.LastTimestamp.Value.AddSeconds(-1)
            : null;
    }

    public async Task<PersistLogBatchResult> PersistBatchAsync(
        IReadOnlyCollection<PendingLogEntry> batch,
        CancellationToken cancellationToken)
    {
        if (batch.Count == 0)
        {
            return new PersistLogBatchResult([], 0);
        }

        await using var db = await CreateDbContextAsync(cancellationToken);
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;
        var targets = batch.Select(entry => entry.Target)
            .GroupBy(target => target.ContainerId, StringComparer.Ordinal)
            .Select(group => group.Last())
            .ToArray();
        var targetIds = targets.Select(target => target.ContainerId).ToArray();
        var streams = await db.LogStreams
            .Where(stream => targetIds.Contains(stream.ContainerId))
            .ToDictionaryAsync(stream => stream.ContainerId, StringComparer.Ordinal, cancellationToken);

        foreach (var target in targets)
        {
            if (!streams.TryGetValue(target.ContainerId, out var stream))
            {
                stream = new LogStreamRecord
                {
                    ContainerId = target.ContainerId,
                    StartedAt = DateTimeOffset.UtcNow
                };
                streams[target.ContainerId] = stream;
                db.LogStreams.Add(stream);
            }

            stream.CurrentDockerId = target.DockerId;
            stream.ContainerName = target.ContainerName;
            stream.Image = target.Image;
            stream.Parser = target.Parser;
            stream.RetentionDays = target.RetentionDays;
            stream.MaxBytes = target.MaxBytes;
            stream.Active = true;
            stream.LastSeenAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);

        var fingerprints = batch.Select(entry => entry.Fingerprint).Distinct(StringComparer.Ordinal).ToArray();
        var existing = (await db.LogEntries.AsNoTracking()
            .Where(entry => fingerprints.Contains(entry.Fingerprint))
            .Select(entry => entry.Fingerprint)
            .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);
        var inserted = new List<LogEntryRecord>();
        foreach (var pending in batch.GroupBy(entry => entry.Fingerprint, StringComparer.Ordinal).Select(group => group.First()))
        {
            if (existing.Contains(pending.Fingerprint))
            {
                continue;
            }

            var record = new LogEntryRecord
            {
                LogStreamId = streams[pending.Target.ContainerId].Id,
                ContainerId = pending.Target.ContainerId,
                DockerId = pending.Target.DockerId,
                ReceivedAt = pending.ReceivedAt,
                Timestamp = pending.Timestamp,
                SourceTimestamp = pending.SourceTimestamp,
                Stream = pending.Stream,
                Line = pending.Line,
                Message = pending.Message,
                Level = pending.Level,
                ExceptionType = pending.ExceptionType,
                TraceId = pending.TraceId,
                ParsedJson = pending.PropertiesJson,
                Fingerprint = pending.Fingerprint,
                SizeBytes = pending.SizeBytes
            };
            inserted.Add(record);
            db.LogEntries.Add(record);
        }

        foreach (var group in batch.GroupBy(entry => entry.Target.ContainerId, StringComparer.Ordinal))
        {
            var latest = group.OrderByDescending(entry => entry.DockerTimestamp).First();
            var checkpoint = await db.LogCheckpoints.SingleOrDefaultAsync(
                entry => entry.ContainerId == group.Key,
                cancellationToken);
            if (checkpoint is null)
            {
                checkpoint = new LogCheckpointRecord { ContainerId = group.Key };
                db.LogCheckpoints.Add(checkpoint);
            }

            checkpoint.DockerId = latest.Target.DockerId;
            checkpoint.LastTimestamp = latest.DockerTimestamp;
            checkpoint.LastFingerprint = latest.Fingerprint;
            checkpoint.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
        var entries = inserted.Select(record => ToDto(record, streams[record.ContainerId].ContainerName)).ToArray();
        return new PersistLogBatchResult(entries, batch.Count - inserted.Count);
    }

    public async Task<LogSearchResponse> SearchAsync(
        string? requiredContainerId,
        LogSearchRequest request,
        CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(request.Limit, 1, 200);
        if (request.From is not null && request.To is not null && request.From > request.To)
        {
            throw new TracebagException(StatusCodes.Status400BadRequest, "invalid_time_range", "The log search start must precede its end.");
        }

        await using var db = await CreateDbContextAsync(cancellationToken);
        var query = db.LogEntries.AsNoTracking().Include(entry => entry.LogStream).AsQueryable();
        if (!string.IsNullOrWhiteSpace(requiredContainerId))
        {
            query = query.Where(entry => entry.ContainerId == requiredContainerId);
        }

        if (!string.IsNullOrWhiteSpace(request.Text))
        {
            var text = request.Text.Trim();
            query = db.Database.IsNpgsql()
                ? query.Where(entry => entry.SearchVector.Matches(EF.Functions.PlainToTsQuery("simple", text)))
                : query.Where(entry => entry.Message.Contains(text) || entry.Line.Contains(text));
        }

        if (!string.IsNullOrWhiteSpace(request.Level))
        {
            var level = request.Level.Trim().ToLowerInvariant();
            query = query.Where(entry => entry.Level == level);
        }

        if (!string.IsNullOrWhiteSpace(request.Stream))
        {
            var stream = request.Stream.Trim().ToLowerInvariant();
            if (stream is not "stdout" and not "stderr")
            {
                throw new TracebagException(StatusCodes.Status400BadRequest, "invalid_log_stream", "Log stream must be stdout or stderr.");
            }

            query = query.Where(entry => entry.Stream == stream);
        }

        if (request.ExceptionOnly)
        {
            query = query.Where(entry => entry.ExceptionType != null);
        }

        if (!string.IsNullOrWhiteSpace(request.TraceId))
        {
            var traceId = request.TraceId.Trim();
            query = query.Where(entry => entry.TraceId == traceId);
        }

        if (request.From is not null)
        {
            query = query.Where(entry => entry.Timestamp >= request.From.Value);
        }

        if (request.To is not null)
        {
            query = query.Where(entry => entry.Timestamp <= request.To.Value);
        }

        var cursor = DecodeCursor(request.Cursor);
        if (cursor is not null)
        {
            query = query.Where(entry => entry.Timestamp < cursor.Timestamp
                || (entry.Timestamp == cursor.Timestamp && entry.Id < cursor.Id));
        }

        var records = await query
            .OrderByDescending(entry => entry.Timestamp)
            .ThenByDescending(entry => entry.Id)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);
        var hasMore = records.Count > limit;
        if (hasMore)
        {
            records.RemoveAt(records.Count - 1);
        }

        var items = records.Select(record => ToDto(record, record.LogStream?.ContainerName ?? record.ContainerId)).ToArray();
        var nextCursor = hasMore && records.Count > 0
            ? EncodeCursor(new LogCursor(records[^1].Timestamp, records[^1].Id))
            : null;
        return new LogSearchResponse(items, nextCursor, hasMore);
    }

    public async Task<IReadOnlyList<LogSearchEntryDto>> ReplayAfterAsync(
        string containerId,
        long afterId,
        int limit,
        CancellationToken cancellationToken)
    {
        await using var db = await CreateDbContextAsync(cancellationToken);
        var records = await db.LogEntries.AsNoTracking().Include(entry => entry.LogStream)
            .Where(entry => entry.ContainerId == containerId && entry.Id > afterId)
            .OrderBy(entry => entry.Id)
            .Take(Math.Clamp(limit, 1, 1_000))
            .ToListAsync(cancellationToken);
        return records.Select(record => ToDto(record, record.LogStream?.ContainerName ?? containerId)).ToArray();
    }

    public async Task<LogStorageSnapshot> StorageSnapshotAsync(CancellationToken cancellationToken)
    {
        await using var db = await CreateDbContextAsync(cancellationToken);
        var count = await db.LogEntries.LongCountAsync(cancellationToken);
        var bytes = count == 0 ? 0 : await db.LogEntries.SumAsync(entry => entry.SizeBytes, cancellationToken);
        var newest = await db.LogEntries.MaxAsync(entry => (DateTimeOffset?)entry.Timestamp, cancellationToken);
        return new LogStorageSnapshot(count, bytes, newest);
    }

    public async Task<int> ApplyRetentionPassAsync(CancellationToken cancellationToken)
    {
        await using var db = await CreateDbContextAsync(cancellationToken);
        var deleted = 0;
        var streams = await db.LogStreams.AsNoTracking().ToListAsync(cancellationToken);
        foreach (var stream in streams)
        {
            var cutoff = DateTimeOffset.UtcNow.AddDays(-Math.Clamp(stream.RetentionDays, 1, options.LogRetentionDays));
            deleted += await DeleteOldestAsync(
                db,
                entry => entry.LogStreamId == stream.Id && entry.Timestamp < cutoff,
                options.LogRetentionDeleteBatchSize,
                cancellationToken);

            var streamBytes = await db.LogEntries
                .Where(entry => entry.LogStreamId == stream.Id)
                .SumAsync(entry => (long?)entry.SizeBytes, cancellationToken) ?? 0;
            if (streamBytes > stream.MaxBytes)
            {
                deleted += await DeleteOldestAsync(
                    db,
                    entry => entry.LogStreamId == stream.Id,
                    options.LogRetentionDeleteBatchSize,
                    cancellationToken);
            }
        }

        var totalBytes = await db.LogEntries.SumAsync(entry => (long?)entry.SizeBytes, cancellationToken) ?? 0;
        if (totalBytes > options.LogMaxTotalBytes)
        {
            deleted += await DeleteOldestAsync(
                db,
                _ => true,
                options.LogRetentionDeleteBatchSize,
                cancellationToken);
        }

        return deleted;
    }

    private static async Task<int> DeleteOldestAsync(
        TracebagDbContext db,
        System.Linq.Expressions.Expression<Func<LogEntryRecord, bool>> predicate,
        int limit,
        CancellationToken cancellationToken)
    {
        var ids = await db.LogEntries.Where(predicate)
            .OrderBy(entry => entry.Timestamp)
            .ThenBy(entry => entry.Id)
            .Select(entry => entry.Id)
            .Take(limit)
            .ToArrayAsync(cancellationToken);
        if (ids.Length == 0)
        {
            return 0;
        }

        if (db.Database.IsRelational())
        {
            await db.LogEntries.Where(entry => ids.Contains(entry.Id)).ExecuteDeleteAsync(cancellationToken);
        }
        else
        {
            var records = await db.LogEntries.Where(entry => ids.Contains(entry.Id)).ToListAsync(cancellationToken);
            db.LogEntries.RemoveRange(records);
            await db.SaveChangesAsync(cancellationToken);
        }

        return ids.Length;
    }

    private static LogSearchEntryDto ToDto(LogEntryRecord record, string containerName)
    {
        IReadOnlyDictionary<string, object?> properties = new Dictionary<string, object?>();
        if (!string.IsNullOrWhiteSpace(record.ParsedJson))
        {
            try
            {
                properties = JsonSerializer.Deserialize<Dictionary<string, object?>>(record.ParsedJson, JsonOptions)
                    ?? new Dictionary<string, object?>();
            }
            catch (JsonException)
            {
                properties = new Dictionary<string, object?>();
            }
        }

        return new LogSearchEntryDto(
            record.Id,
            record.ContainerId,
            containerName,
            record.DockerId,
            record.Timestamp,
            record.ReceivedAt,
            record.Stream,
            record.Line,
            record.Message,
            record.Level,
            record.ExceptionType,
            record.TraceId,
            properties);
    }

    private async Task<TracebagDbContext> CreateDbContextAsync(CancellationToken cancellationToken)
    {
        if (dbContextFactory is null)
        {
            throw new TracebagException(
                StatusCodes.Status503ServiceUnavailable,
                "log_storage_unavailable",
                "Persistent log storage requires PostgreSQL.");
        }

        return await dbContextFactory.CreateDbContextAsync(cancellationToken);
    }

    private static string EncodeCursor(LogCursor cursor)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(cursor, JsonOptions)))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static LogCursor? DecodeCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return null;
        }

        try
        {
            var normalized = cursor.Replace('-', '+').Replace('_', '/');
            normalized = normalized.PadRight(normalized.Length + ((4 - normalized.Length % 4) % 4), '=');
            return JsonSerializer.Deserialize<LogCursor>(Convert.FromBase64String(normalized), JsonOptions)
                ?? throw new JsonException();
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            throw new TracebagException(StatusCodes.Status400BadRequest, "invalid_log_cursor", "The log search cursor is invalid.");
        }
    }

    private sealed record LogCursor(DateTimeOffset Timestamp, long Id);
}

public sealed record PersistLogBatchResult(IReadOnlyList<LogSearchEntryDto> Entries, int DuplicateCount);
public sealed record LogStorageSnapshot(long EntryCount, long SizeBytes, DateTimeOffset? NewestTimestamp);
