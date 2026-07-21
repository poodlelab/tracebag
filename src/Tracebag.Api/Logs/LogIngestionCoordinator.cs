using System.Collections.Concurrent;
using System.Threading.Channels;
using Tracebag.Api.Auth;
using Tracebag.Api.Docker;
using Tracebag.Api.Models;

namespace Tracebag.Api.Logs;

public sealed class LogIngestionCoordinator : BackgroundService
{
    private readonly TracebagOptions _options;
    private readonly ContainerCatalog _containerCatalog;
    private readonly LogTargetPolicy _targetPolicy;
    private readonly DockerLogService _dockerLogService;
    private readonly LogParserChain _parserChain;
    private readonly LogStore _store;
    private readonly LogLiveHub _liveHub;
    private readonly ILogger<LogIngestionCoordinator> _logger;
    private readonly Channel<PendingLogEntry> _channel;
    private readonly ConcurrentDictionary<string, Collector> _collectors = new(StringComparer.Ordinal);
    private long _queueDepth;
    private long _persistedEntries;
    private long _droppedEntries;
    private long _duplicateEntries;
    private long _retentionDeletedEntries;
    private DateTimeOffset? _lastPersistedAt;
    private string? _lastError;

    public LogIngestionCoordinator(
        TracebagOptions options,
        ContainerCatalog containerCatalog,
        LogTargetPolicy targetPolicy,
        DockerLogService dockerLogService,
        LogParserChain parserChain,
        LogStore store,
        LogLiveHub liveHub,
        ILogger<LogIngestionCoordinator> logger)
    {
        _options = options;
        _containerCatalog = containerCatalog;
        _targetPolicy = targetPolicy;
        _dockerLogService = dockerLogService;
        _parserChain = parserChain;
        _store = store;
        _liveHub = liveHub;
        _logger = logger;
        _channel = Channel.CreateBounded<PendingLogEntry>(new BoundedChannelOptions(options.LogChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public void RecordRetentionDeletion(int count)
    {
        Interlocked.Add(ref _retentionDeletedEntries, count);
    }

    public async Task<LogIngestionStatusDto> StatusAsync(CancellationToken cancellationToken)
    {
        if (!_options.DatabaseEnabled || !_options.LogIngestionEnabled)
        {
            return new LogIngestionStatusDto(
                "disabled", 0, 0, _options.LogChannelCapacity, 0, 0, 0, 0, 0, 0,
                null, null, null,
                _options.DatabaseEnabled ? "Log ingestion is disabled by configuration." : "Persistent log ingestion requires PostgreSQL.");
        }

        try
        {
            var storage = await _store.StorageSnapshotAsync(cancellationToken);
            double? lag = storage.NewestTimestamp is null
                ? null
                : Math.Max(0, (DateTimeOffset.UtcNow - storage.NewestTimestamp.Value).TotalSeconds);
            return new LogIngestionStatusDto(
                string.IsNullOrWhiteSpace(_lastError) ? "healthy" : "degraded",
                _collectors.Count,
                (int)Math.Max(0, Interlocked.Read(ref _queueDepth)),
                _options.LogChannelCapacity,
                Interlocked.Read(ref _persistedEntries),
                Interlocked.Read(ref _droppedEntries),
                Interlocked.Read(ref _duplicateEntries),
                Interlocked.Read(ref _retentionDeletedEntries),
                storage.EntryCount,
                storage.SizeBytes,
                _lastPersistedAt,
                storage.NewestTimestamp,
                lag,
                _lastError);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return new LogIngestionStatusDto(
                "unavailable",
                _collectors.Count,
                (int)Math.Max(0, Interlocked.Read(ref _queueDepth)),
                _options.LogChannelCapacity,
                Interlocked.Read(ref _persistedEntries),
                Interlocked.Read(ref _droppedEntries),
                Interlocked.Read(ref _duplicateEntries),
                Interlocked.Read(ref _retentionDeletedEntries),
                0,
                0,
                _lastPersistedAt,
                null,
                null,
                "Log storage is temporarily unavailable.");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var writer = WriteBatchesAsync(stoppingToken);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await ReconcileCollectorsAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(_options.LogCollectorScanSeconds), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        finally
        {
            await Task.WhenAll(_collectors.Values.Select(StopCollectorAsync));
            _collectors.Clear();
            _channel.Writer.TryComplete();
            try
            {
                await writer;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Checkpoints only advance after committed batches, so queued entries replay after restart.
            }
        }
    }

    private async Task ReconcileCollectorsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var containers = await _containerCatalog.ListAllowedContainersAsync(cancellationToken);
            var targets = containers
                .Select(_targetPolicy.Resolve)
                .Where(target => target is not null)
                .Cast<LogTarget>()
                .ToDictionary(target => target.ContainerId, StringComparer.Ordinal);

            foreach (var existing in _collectors.ToArray())
            {
                if (!targets.TryGetValue(existing.Key, out var target)
                    || target.DockerId != existing.Value.Target.DockerId)
                {
                    if (_collectors.TryRemove(existing.Key, out var removed))
                    {
                        _ = StopCollectorAsync(removed);
                    }
                }
            }

            foreach (var target in targets.Values)
            {
                _collectors.GetOrAdd(target.ContainerId, _ => StartCollector(target, cancellationToken));
            }
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            _lastError = "Container discovery for log ingestion failed; retrying.";
            _logger.LogWarning(exception, "Log collector discovery failed; retrying.");
        }
    }

    private Collector StartCollector(LogTarget target, CancellationToken stoppingToken)
    {
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var task = CollectAsync(target, cancellation.Token);
        return new Collector(target, cancellation, task);
    }

    private async Task CollectAsync(LogTarget target, CancellationToken cancellationToken)
    {
        DateTimeOffset? since = null;
        try
        {
            since = await _store.GetResumeTimestampAsync(target.ContainerId, target.DockerId, cancellationToken)
                ?? target.CreatedAt.AddSeconds(-1);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            _lastError = "Log checkpoint storage is unavailable; collector will retry.";
            _logger.LogWarning(exception, "Could not read log checkpoint for {ContainerId}.", target.ContainerId);
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var stream = await _dockerLogService.OpenIngestionStreamAsync(target.DockerId, since, cancellationToken);
                await foreach (var logEvent in DockerLogService.ReadEventsAsync(
                    stream,
                    _options.LogMaxLineBytes,
                    cancellationToken))
                {
                    var receivedAt = DateTimeOffset.UtcNow;
                    var pending = PendingLogEntry.Create(target, logEvent.Stream, logEvent.Line, receivedAt, _parserChain);
                    since = pending.DockerTimestamp.AddSeconds(-1);
                    if (_channel.Writer.TryWrite(pending))
                    {
                        Interlocked.Increment(ref _queueDepth);
                    }
                    else
                    {
                        Interlocked.Increment(ref _droppedEntries);
                        _lastError = "The bounded log queue is full; entries are being dropped.";
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _lastError = "A Docker log stream disconnected; retrying.";
                _logger.LogWarning(exception, "Docker log stream disconnected for {ContainerId}; retrying.", target.ContainerId);
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }
    }

    private async Task WriteBatchesAsync(CancellationToken cancellationToken)
    {
        var batch = new List<PendingLogEntry>(_options.LogBatchSize);
        while (await _channel.Reader.WaitToReadAsync(cancellationToken))
        {
            batch.Clear();
            if (_channel.Reader.TryRead(out var first))
            {
                batch.Add(first);
                Interlocked.Decrement(ref _queueDepth);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(_options.LogFlushMilliseconds), cancellationToken);
            while (batch.Count < _options.LogBatchSize && _channel.Reader.TryRead(out var item))
            {
                batch.Add(item);
                Interlocked.Decrement(ref _queueDepth);
            }

            while (batch.Count > 0 && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var result = await _store.PersistBatchAsync(batch, cancellationToken);
                    Interlocked.Add(ref _persistedEntries, result.Entries.Count);
                    Interlocked.Add(ref _duplicateEntries, result.DuplicateCount);
                    _lastPersistedAt = DateTimeOffset.UtcNow;
                    _lastError = null;
                    _liveHub.Publish(result.Entries);
                    batch.Clear();
                }
                catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
                {
                    _lastError = "Log database writes are failing; queued entries are waiting and ingestion may drop new data.";
                    _logger.LogWarning(exception, "Log batch persistence failed; retrying.");
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                }
            }
        }
    }

    private static async Task StopCollectorAsync(Collector collector)
    {
        collector.Cancellation.Cancel();
        try
        {
            await collector.Task;
        }
        catch (OperationCanceledException)
        {
            // Expected when a target is removed, recreated, or Tracebag stops.
        }
        finally
        {
            collector.Cancellation.Dispose();
        }
    }

    private sealed record Collector(LogTarget Target, CancellationTokenSource Cancellation, Task Task);
}
