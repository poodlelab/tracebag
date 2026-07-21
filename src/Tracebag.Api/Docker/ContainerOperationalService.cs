using Docker.DotNet.Models;
using Microsoft.EntityFrameworkCore;
using Tracebag.Api.Data;
using Tracebag.Api.Models;

namespace Tracebag.Api.Docker;

public sealed class ContainerOperationalService(
    DockerClientFactory dockerClientFactory,
    ContainerCatalog containerCatalog,
    ContainerPolicy containerPolicy,
    DockerEventCollector eventCollector,
    IDbContextFactory<TracebagDbContext>? dbContextFactory = null)
{
    public async Task<ContainerOverviewDto> GetOverviewAsync(
        string containerReference,
        CancellationToken cancellationToken)
    {
        var container = await containerCatalog.GetAllowedAsync(containerReference, cancellationToken);
        var identity = containerPolicy.GetIdentity(container);
        var inspect = await dockerClientFactory.Client.Containers.InspectContainerAsync(container.ID, cancellationToken);
        var resources = inspect.State.Running
            ? await ReadStatsAsync(container.ID, cancellationToken)
            : UnavailableStats("Container is not running.");
        var instanceCount = await CountInstancesAsync(identity.Id, cancellationToken);
        var events = await eventCollector.ListAsync(identity.Id, 50, cancellationToken);

        return new ContainerOverviewDto(
            containerPolicy.ToDto(container),
            ToInspectDto(inspect),
            resources,
            events,
            instanceCount);
    }

    private async Task<ContainerResourceStatsDto> ReadStatsAsync(
        string dockerId,
        CancellationToken cancellationToken)
    {
        ContainerStatsResponse? snapshot = null;
        try
        {
            await dockerClientFactory.Client.Containers.GetContainerStatsAsync(
                dockerId,
                new ContainerStatsParameters { Stream = false, OneShot = true },
                new InlineProgress<ContainerStatsResponse>(value => snapshot = value),
                cancellationToken);
            return snapshot is null ? UnavailableStats("Docker returned no resource snapshot.") : ToStatsDto(snapshot);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return UnavailableStats("Docker resource statistics are temporarily unavailable.");
        }
    }

    private async Task<int> CountInstancesAsync(string identityId, CancellationToken cancellationToken)
    {
        if (dbContextFactory is null)
        {
            return 1;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.ContainerInstances.CountAsync(
            instance => instance.ContainerTargetId == identityId,
            cancellationToken);
    }

    internal static ContainerResourceStatsDto ToStatsDto(ContainerStatsResponse stats)
    {
        var cpuDelta = Subtract(stats.CPUStats?.CPUUsage?.TotalUsage ?? 0, stats.PreCPUStats?.CPUUsage?.TotalUsage ?? 0);
        var systemDelta = Subtract(stats.CPUStats?.SystemUsage ?? 0, stats.PreCPUStats?.SystemUsage ?? 0);
        var onlineCpus = stats.CPUStats?.OnlineCPUs > 0
            ? stats.CPUStats.OnlineCPUs
            : (uint)(stats.CPUStats?.CPUUsage?.PercpuUsage?.Count ?? 1);
        var cpuPercent = systemDelta > 0 && cpuDelta > 0
            ? (double)cpuDelta / systemDelta * onlineCpus * 100
            : 0;

        var memory = stats.MemoryStats?.Usage ?? 0;
        var cache = ReadMemoryCache(stats.MemoryStats?.Stats);
        var memoryUsage = Subtract(memory, cache);
        var memoryLimit = stats.MemoryStats?.Limit ?? 0;
        var memoryPercent = memoryLimit > 0 ? (double)memoryUsage / memoryLimit * 100 : 0;
        var networks = stats.Networks?.Values ?? Array.Empty<NetworkStats>();
        var blockEntries = stats.BlkioStats?.IoServiceBytesRecursive ?? Array.Empty<BlkioStatEntry>();

        return new ContainerResourceStatsDto(
            true,
            null,
            stats.Read == default ? DateTimeOffset.UtcNow : new DateTimeOffset(DateTime.SpecifyKind(stats.Read, DateTimeKind.Utc)),
            Math.Round(cpuPercent, 2),
            memoryUsage,
            memoryLimit,
            Math.Round(memoryPercent, 2),
            SumSaturating(networks.Select(network => network.RxBytes)),
            SumSaturating(networks.Select(network => network.TxBytes)),
            SumBlockOperation(blockEntries, "read"),
            SumBlockOperation(blockEntries, "write"),
            stats.PidsStats?.Current);
    }

    private static ContainerInspectDto ToInspectDto(ContainerInspectResponse inspect)
    {
        var state = inspect.State;
        var health = state.Health;
        var healthLogs = health?.Log?
            .TakeLast(5)
            .Select(entry => new ContainerHealthLogDto(
                new DateTimeOffset(DateTime.SpecifyKind(entry.Start, DateTimeKind.Utc)),
                new DateTimeOffset(DateTime.SpecifyKind(entry.End, DateTimeKind.Utc)),
                entry.ExitCode,
                Truncate(entry.Output?.Trim(), 1_000)))
            .ToArray() ?? [];

        return new ContainerInspectDto(
            inspect.Platform ?? "unknown",
            inspect.Driver ?? "unknown",
            state.Running,
            state.Paused,
            state.Restarting,
            state.Dead,
            state.OOMKilled,
            state.Pid,
            state.ExitCode,
            inspect.RestartCount,
            ParseDockerTimestamp(state.StartedAt),
            ParseDockerTimestamp(state.FinishedAt),
            new ContainerHealthDto(
                health?.Status ?? "not-configured",
                health?.FailingStreak ?? 0,
                healthLogs));
    }

    private static ContainerResourceStatsDto UnavailableStats(string reason)
    {
        return new ContainerResourceStatsDto(
            false,
            reason,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null);
    }

    private static DateTimeOffset? ParseDockerTimestamp(string? value)
    {
        return DateTimeOffset.TryParse(value, out var parsed) && parsed.Year > 1 ? parsed : null;
    }

    private static ulong ReadMemoryCache(IDictionary<string, ulong>? values)
    {
        if (values is null)
        {
            return 0;
        }

        if (values.TryGetValue("inactive_file", out var inactiveFile))
        {
            return inactiveFile;
        }

        return values.TryGetValue("cache", out var cache) ? cache : 0;
    }

    private static ulong SumBlockOperation(IEnumerable<BlkioStatEntry> entries, string operation)
    {
        return SumSaturating(entries
            .Where(entry => string.Equals(entry.Op, operation, StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry.Value));
    }

    private static ulong SumSaturating(IEnumerable<ulong> values)
    {
        var total = 0UL;
        foreach (var value in values)
        {
            total = ulong.MaxValue - total < value ? ulong.MaxValue : total + value;
        }

        return total;
    }

    private static ulong Subtract(ulong value, ulong subtract) => value >= subtract ? value - subtract : 0;

    private static string Truncate(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Length <= maximumLength ? value : value[..maximumLength];
    }
}
