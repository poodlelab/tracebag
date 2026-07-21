using Docker.DotNet.Models;
using Microsoft.EntityFrameworkCore;
using Tracebag.Api.Data;

namespace Tracebag.Api.Docker;

public sealed class ContainerTargetRegistry : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, ContainerTargetSnapshot> _targets = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _persistLock = new(1, 1);
    private readonly ContainerPolicy _policy;
    private readonly IDbContextFactory<TracebagDbContext>? _dbContextFactory;

    public ContainerTargetRegistry(
        ContainerPolicy policy,
        IDbContextFactory<TracebagDbContext>? dbContextFactory = null)
    {
        _policy = policy;
        _dbContextFactory = dbContextFactory;
    }

    public async Task ReconcileAsync(
        IReadOnlyCollection<ContainerListResponse> containers,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var observed = containers.Select(container => ToSnapshot(container, now)).ToArray();
        lock (_gate)
        {
            var activeIds = observed.Select(target => target.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var existing in _targets.Values.Where(target => !activeIds.Contains(target.Id)).ToArray())
            {
                _targets[existing.Id] = existing with { Active = false, CurrentDockerId = null, LastSeenAt = now };
            }

            foreach (var target in observed)
            {
                if (_targets.TryGetValue(target.Id, out var existing))
                {
                    _targets[target.Id] = target with { FirstSeenAt = existing.FirstSeenAt };
                }
                else
                {
                    _targets[target.Id] = target;
                }
            }
        }

        if (_dbContextFactory is not null)
        {
            await _persistLock.WaitAsync(cancellationToken);
            try
            {
                await PersistReconciliationAsync(observed, now, cancellationToken);
            }
            finally
            {
                _persistLock.Release();
            }
        }
    }

    public IReadOnlyList<ContainerTargetSnapshot> Snapshot()
    {
        lock (_gate)
        {
            return _targets.Values.OrderBy(target => target.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
        }
    }

    public void Dispose()
    {
        _persistLock.Dispose();
    }

    private ContainerTargetSnapshot ToSnapshot(ContainerListResponse container, DateTimeOffset now)
    {
        var dto = _policy.ToDto(container);
        return new ContainerTargetSnapshot(
            dto.Id,
            dto.IdentitySource,
            dto.DockerId,
            dto.Name,
            dto.DisplayName,
            dto.ProjectName,
            dto.ServiceName,
            _policy.GetIdentity(container).ComposeReplica,
            dto.Kind,
            dto.Image,
            now,
            now,
            true,
            1)
        {
            CreatedAt = dto.Created
        };
    }

    private async Task PersistReconciliationAsync(
        IReadOnlyCollection<ContainerTargetSnapshot> observed,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory!.CreateDbContextAsync(cancellationToken);
        var activeIds = observed.Select(target => target.Id).ToArray();
        var inactiveTargets = await db.ContainerTargets
            .Where(target => target.Active && !activeIds.Contains(target.Id))
            .ToListAsync(cancellationToken);
        foreach (var inactive in inactiveTargets)
        {
            if (inactive.CurrentDockerId is not null)
            {
                var oldInstance = await db.ContainerInstances.FindAsync([inactive.CurrentDockerId], cancellationToken);
                if (oldInstance is not null)
                {
                    oldInstance.RemovedAt ??= now;
                    oldInstance.LastSeenAt = now;
                }
            }

            inactive.Active = false;
            inactive.CurrentDockerId = null;
            inactive.LastSeenAt = now;
        }

        foreach (var observedTarget in observed)
        {
            var target = await db.ContainerTargets.FindAsync([observedTarget.Id], cancellationToken);
            if (target is null)
            {
                target = new ContainerTargetRecord
                {
                    Id = observedTarget.Id,
                    FirstSeenAt = now
                };
                db.ContainerTargets.Add(target);
            }
            else if (target.CurrentDockerId is not null && target.CurrentDockerId != observedTarget.CurrentDockerId)
            {
                var replaced = await db.ContainerInstances.FindAsync([target.CurrentDockerId], cancellationToken);
                if (replaced is not null)
                {
                    replaced.RemovedAt ??= now;
                    replaced.LastSeenAt = now;
                }
            }

            target.IdentitySource = observedTarget.IdentitySource;
            target.CurrentDockerId = observedTarget.CurrentDockerId;
            target.Name = observedTarget.Name;
            target.DisplayName = observedTarget.DisplayName;
            target.ComposeProject = observedTarget.ComposeProject;
            target.ComposeService = observedTarget.ComposeService;
            target.ComposeReplica = observedTarget.ComposeReplica;
            target.Kind = observedTarget.Kind;
            target.Image = observedTarget.Image;
            target.LastSeenAt = now;
            target.Active = true;

            var instance = await db.ContainerInstances.FindAsync([observedTarget.CurrentDockerId], cancellationToken);
            if (instance is null)
            {
                instance = new ContainerInstanceRecord
                {
                    DockerId = observedTarget.CurrentDockerId!,
                    ContainerTargetId = observedTarget.Id,
                    Name = observedTarget.Name,
                    Image = observedTarget.Image,
                    CreatedAt = observedTarget.CreatedAt,
                    FirstSeenAt = now
                };
                db.ContainerInstances.Add(instance);
            }

            instance.LastSeenAt = now;
            instance.RemovedAt = null;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}

public sealed record ContainerTargetSnapshot(
    string Id,
    string IdentitySource,
    string? CurrentDockerId,
    string Name,
    string DisplayName,
    string? ComposeProject,
    string? ComposeService,
    string? ComposeReplica,
    string Kind,
    string Image,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    bool Active,
    int InstanceCount)
{
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
