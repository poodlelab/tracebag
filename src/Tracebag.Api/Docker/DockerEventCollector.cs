using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using Docker.DotNet.Models;
using Microsoft.EntityFrameworkCore;
using Tracebag.Api.Data;
using Tracebag.Api.Models;

namespace Tracebag.Api.Docker;

public sealed class DockerEventCollector(
    DockerClientFactory dockerClientFactory,
    ContainerPolicy containerPolicy,
    ContainerIdentityResolver identityResolver,
    ILogger<DockerEventCollector> logger,
    IDbContextFactory<TracebagDbContext>? dbContextFactory = null) : BackgroundService
{
    private const int MemoryEventLimit = 500;
    private const int DatabaseEventLimit = 2_000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentQueue<DockerEventDto> _events = new();

    public bool Connected { get; private set; }
    public DateTimeOffset? LastConnectedAt { get; private set; }
    public DateTimeOffset? LastEventAt { get; private set; }
    public string? LastError { get; private set; }
    public int RetainedEventCount => _events.Count;

    public async Task<IReadOnlyList<DockerEventDto>> ListAsync(
        string? containerIdentityId,
        int limit,
        CancellationToken cancellationToken)
    {
        var boundedLimit = Math.Clamp(limit, 1, 200);
        if (dbContextFactory is not null)
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var query = db.DockerEvents.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(containerIdentityId))
            {
                query = query.Where(entry => entry.ContainerTargetId == containerIdentityId);
            }

            var records = await query
                .OrderByDescending(entry => entry.Timestamp)
                .Take(boundedLimit)
                .ToListAsync(cancellationToken);
            return records.Select(ToDto).ToArray();
        }

        return _events
            .Where(entry => string.IsNullOrWhiteSpace(containerIdentityId) || entry.ContainerId == containerIdentityId)
            .OrderByDescending(entry => entry.Timestamp)
            .Take(boundedLimit)
            .ToArray();
    }

    public EventCollectorStatusDto Status()
    {
        return new EventCollectorStatusDto(
            Connected ? "connected" : "disconnected",
            LastConnectedAt,
            LastEventAt,
            RetainedEventCount,
            LastError);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await MonitorOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                Connected = false;
                LastError = "Docker event stream disconnected; retrying.";
                logger.LogWarning(exception, "Docker event stream disconnected; retrying.");
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
    }

    private async Task MonitorOnceAsync(CancellationToken cancellationToken)
    {
        var channel = Channel.CreateBounded<Message>(new BoundedChannelOptions(256)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
        var progress = new InlineProgress<Message>(message => channel.Writer.TryWrite(message));
        var consumeTask = ConsumeAsync(channel.Reader, cancellationToken);
        Connected = true;
        LastConnectedAt = DateTimeOffset.UtcNow;
        LastError = null;

        try
        {
            await dockerClientFactory.Client.System.MonitorEventsAsync(
                new ContainerEventsParameters
                {
                    Filters = new Dictionary<string, IDictionary<string, bool>>
                    {
                        ["type"] = new Dictionary<string, bool> { ["container"] = true }
                    }
                },
                progress,
                cancellationToken);
        }
        finally
        {
            Connected = false;
            channel.Writer.TryComplete();
            await consumeTask;
        }
    }

    private async Task ConsumeAsync(ChannelReader<Message> reader, CancellationToken cancellationToken)
    {
        await foreach (var message in reader.ReadAllAsync(cancellationToken))
        {
            if (!string.Equals(message.Type, "container", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var attributes = message.Actor?.Attributes ?? new Dictionary<string, string>();
            var dockerId = message.Actor?.ID ?? message.ID ?? string.Empty;
            attributes.TryGetValue("name", out var name);
            if (string.IsNullOrWhiteSpace(dockerId)
                || !containerPolicy.IsAllowedEvent(dockerId, name ?? string.Empty, attributes))
            {
                continue;
            }

            var identity = identityResolver.Resolve(dockerId, name, attributes);
            var timestamp = message.Time > 0
                ? DateTimeOffset.FromUnixTimeSeconds(message.Time)
                : DateTimeOffset.UtcNow;
            var safeAttributes = SelectSafeAttributes(attributes);
            var entry = new DockerEventDto(
                null,
                identity.Id,
                dockerId,
                message.Action ?? message.Status ?? "unknown",
                timestamp,
                safeAttributes);
            _events.Enqueue(entry);
            while (_events.Count > MemoryEventLimit)
            {
                _events.TryDequeue(out _);
            }

            LastEventAt = timestamp;
            if (dbContextFactory is not null)
            {
                await PersistAsync(entry, cancellationToken);
            }
        }
    }

    private async Task PersistAsync(DockerEventDto entry, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory!.CreateDbContextAsync(cancellationToken);
        db.DockerEvents.Add(new DockerEventRecord
        {
            ContainerTargetId = entry.ContainerId,
            DockerId = entry.DockerId,
            Action = entry.Action,
            Timestamp = entry.Timestamp,
            AttributesJson = JsonSerializer.Serialize(entry.Attributes, JsonOptions)
        });
        await db.SaveChangesAsync(cancellationToken);

        var expiredIds = await db.DockerEvents
            .OrderByDescending(item => item.Timestamp)
            .Skip(DatabaseEventLimit)
            .Select(item => item.Id)
            .ToListAsync(cancellationToken);
        if (expiredIds.Count > 0)
        {
            await db.DockerEvents.Where(item => expiredIds.Contains(item.Id)).ExecuteDeleteAsync(cancellationToken);
        }
    }

    private static Dictionary<string, string> SelectSafeAttributes(IDictionary<string, string> attributes)
    {
        var allowedKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "name",
            "image",
            "exitCode",
            "signal",
            "health_status",
            "com.docker.compose.project",
            "com.docker.compose.service",
            "com.docker.compose.container-number",
            "tracebag.identity",
            "tracebag.displayName",
            "tracebag.kind"
        };
        return attributes
            .Where(attribute => allowedKeys.Contains(attribute.Key))
            .ToDictionary(attribute => attribute.Key, attribute => attribute.Value, StringComparer.Ordinal);
    }

    private static DockerEventDto ToDto(DockerEventRecord record)
    {
        var attributes = string.IsNullOrWhiteSpace(record.AttributesJson)
            ? new Dictionary<string, string>()
            : JsonSerializer.Deserialize<Dictionary<string, string>>(record.AttributesJson, JsonOptions)
                ?? new Dictionary<string, string>();
        return new DockerEventDto(
            record.Id,
            record.ContainerTargetId,
            record.DockerId,
            record.Action,
            record.Timestamp,
            attributes);
    }
}
