using Docker.DotNet.Models;
using Tracebag.Api.Models;
using Tracebag.Api.Security;

namespace Tracebag.Api.Docker;

public sealed class ContainerCatalog
{
    private readonly DockerClientFactory _dockerClientFactory;
    private readonly ContainerPolicy _policy;
    private readonly ContainerTargetRegistry _targetRegistry;

    public ContainerCatalog(
        DockerClientFactory dockerClientFactory,
        ContainerPolicy policy,
        ContainerTargetRegistry targetRegistry)
    {
        _dockerClientFactory = dockerClientFactory;
        _policy = policy;
        _targetRegistry = targetRegistry;
    }

    public async Task<IReadOnlyList<ContainerDto>> ListAllowedAsync(CancellationToken cancellationToken)
    {
        var allowed = await ListAllowedContainersAsync(cancellationToken);
        return allowed
            .Select(_policy.ToDto)
            .OrderBy(container => container.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<IReadOnlyList<ContainerListResponse>> ListAllowedContainersAsync(CancellationToken cancellationToken)
    {
        var containers = await ListAllAsync(cancellationToken);
        var allowed = containers.Where(_policy.IsAllowed).ToArray();
        await _targetRegistry.ReconcileAsync(allowed, cancellationToken);
        return allowed;
    }

    public async Task<ContainerListResponse> GetAllowedAsync(string containerId, CancellationToken cancellationToken)
    {
        var container = await FindAsync(containerId, cancellationToken);
        _policy.EnsureAllowed(container);
        return container;
    }

    public async Task<ContainerListResponse> GetAllowedDotnetAsync(string containerId, CancellationToken cancellationToken)
    {
        var container = await GetAllowedAsync(containerId, cancellationToken);
        _policy.EnsureDotnet(container);
        return container;
    }

    private async Task<ContainerListResponse> FindAsync(string containerId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(containerId))
        {
            throw new TracebagException(StatusCodes.Status404NotFound, "container_not_found", "The requested container was not found.");
        }

        var containers = await ListAllAsync(cancellationToken);
        var match = containers.FirstOrDefault(container =>
            string.Equals(container.ID, containerId, StringComparison.Ordinal)
            || container.ID.StartsWith(containerId, StringComparison.Ordinal)
            || container.Names.Any(name => string.Equals(name.TrimStart('/'), containerId, StringComparison.OrdinalIgnoreCase))
            || string.Equals(_policy.GetIdentity(container).Id, containerId, StringComparison.Ordinal));

        return match ?? throw new TracebagException(StatusCodes.Status404NotFound, "container_not_found", "The requested container was not found.");
    }

    private async Task<IReadOnlyList<ContainerListResponse>> ListAllAsync(CancellationToken cancellationToken)
    {
        var containers = await _dockerClientFactory.Client.Containers.ListContainersAsync(
            new ContainersListParameters { All = true },
            cancellationToken);
        return containers.ToArray();
    }
}
