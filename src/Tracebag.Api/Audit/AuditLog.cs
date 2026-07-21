using System.Text.Json;
using Tracebag.Api.Auth;
using Tracebag.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Tracebag.Api.Audit;

public sealed class AuditLog : IDisposable
{
    private const int MaximumRetentionBatchesPerPass = 10;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TracebagOptions _options;
    private readonly IDbContextFactory<TracebagDbContext>? _dbContextFactory;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public AuditLog(TracebagOptions options, IDbContextFactory<TracebagDbContext>? dbContextFactory = null)
    {
        _options = options;
        _dbContextFactory = dbContextFactory;
    }

    public async Task WriteAsync(
        string? user,
        string action,
        string? targetContainerId,
        string? targetContainerName,
        string result,
        object? metadata,
        CancellationToken cancellationToken)
    {
        var timestamp = DateTimeOffset.UtcNow;
        var normalizedUser = Normalize(user, 160, "anonymous");

        if (_dbContextFactory is not null)
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            db.AuditEvents.Add(new AuditEventRecord
            {
                Timestamp = timestamp,
                User = normalizedUser,
                Action = Normalize(action, 120, "unknown"),
                TargetContainerId = NormalizeOptional(targetContainerId, 128),
                TargetContainerName = NormalizeOptional(targetContainerName, 200),
                Result = Normalize(result, 40, "unknown"),
                MetadataJson = metadata is null ? null : JsonSerializer.Serialize(metadata, JsonOptions)
            });
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var entry = new
        {
            timestamp,
            user = normalizedUser,
            action,
            targetContainerId,
            targetContainerName,
            result,
            metadata
        };

        var line = JsonSerializer.Serialize(entry, JsonOptions);
        var path = Path.Combine(_options.DataDir, "audit.log");
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await File.AppendAllTextAsync(path, line + Environment.NewLine, cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<AuditRetentionResult> ApplyRetentionPassAsync(CancellationToken cancellationToken)
    {
        if (_dbContextFactory is null)
        {
            return new AuditRetentionResult(0, 0);
        }

        var cutoff = DateTimeOffset.UtcNow.AddDays(-_options.AuditRetentionDays);
        var expiredDeleted = 0;
        for (var batch = 0; batch < MaximumRetentionBatchesPerPass; batch++)
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            var deleted = await DeleteOldestAsync(
                db,
                auditEvent => auditEvent.Timestamp < cutoff,
                _options.AuditRetentionDeleteBatchSize,
                cancellationToken);
            expiredDeleted += deleted;
            if (deleted < _options.AuditRetentionDeleteBatchSize)
            {
                break;
            }
        }

        var overflowDeleted = 0;
        for (var batch = 0; batch < MaximumRetentionBatchesPerPass; batch++)
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            var count = await db.AuditEvents.LongCountAsync(cancellationToken);
            var overflow = count - _options.AuditMaxEvents;
            if (overflow <= 0)
            {
                break;
            }

            var deleted = await DeleteOldestAsync(
                db,
                _ => true,
                (int)Math.Min(overflow, _options.AuditRetentionDeleteBatchSize),
                cancellationToken);
            overflowDeleted += deleted;
            if (deleted == 0)
            {
                break;
            }
        }

        return new AuditRetentionResult(expiredDeleted, overflowDeleted);
    }

    private static async Task<int> DeleteOldestAsync(
        TracebagDbContext db,
        System.Linq.Expressions.Expression<Func<AuditEventRecord, bool>> predicate,
        int limit,
        CancellationToken cancellationToken)
    {
        var ids = await db.AuditEvents
            .Where(predicate)
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
            await db.AuditEvents.Where(entry => ids.Contains(entry.Id)).ExecuteDeleteAsync(cancellationToken);
        }
        else
        {
            var records = await db.AuditEvents.Where(entry => ids.Contains(entry.Id)).ToArrayAsync(cancellationToken);
            db.AuditEvents.RemoveRange(records);
            await db.SaveChangesAsync(cancellationToken);
        }

        return ids.Length;
    }

    private static string Normalize(string? value, int maximumLength, string fallback)
    {
        value = value?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return value.Length <= maximumLength ? value : value[..maximumLength];
    }

    private static string? NormalizeOptional(string? value, int maximumLength)
    {
        value = value?.Trim();
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Length <= maximumLength ? value : value[..maximumLength];
    }

    public void Dispose()
    {
        _writeLock.Dispose();
    }
}

public sealed record AuditRetentionResult(int ExpiredDeleted, int OverflowDeleted);
