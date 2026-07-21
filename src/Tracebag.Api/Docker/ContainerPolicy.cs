using Docker.DotNet.Models;
using Tracebag.Api.Auth;
using Tracebag.Api.Models;
using Tracebag.Api.Security;

namespace Tracebag.Api.Docker;

public sealed class ContainerPolicy
{
    private readonly TracebagOptions _options;
    private readonly ContainerIdentityResolver _identityResolver;

    public ContainerPolicy(TracebagOptions options, ContainerIdentityResolver? identityResolver = null)
    {
        _options = options;
        _identityResolver = identityResolver ?? new ContainerIdentityResolver();
    }

    public bool IsAllowed(ContainerListResponse container)
    {
        var labels = container.Labels ?? new Dictionary<string, string>();
        return HasLabel(labels, _options.AllowedLabelKey, _options.AllowedLabelValue)
            && MatchesEnvironment(labels)
            && !IsBlocked(container.ID, labels, GetContainerName(container), GetServiceName(labels));
    }

    public void EnsureAllowed(ContainerListResponse container)
    {
        if (!IsAllowed(container))
        {
            throw new TracebagException(StatusCodes.Status404NotFound, "container_not_allowed", "The requested container is not allowlisted for Tracebag.");
        }
    }

    public void EnsureDotnet(ContainerListResponse container)
    {
        EnsureAllowed(container);
        var kind = GetKind(container.Labels);
        if (!string.Equals(kind, "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            throw new TracebagException(StatusCodes.Status400BadRequest, "container_not_dotnet", "The requested container is not marked as a .NET container.");
        }
    }

    public void EnsureRestartAllowed(ContainerListResponse container)
    {
        EnsureAllowed(container);
        var labels = container.Labels ?? new Dictionary<string, string>();
        if (!_options.RestartEnabled || !HasLabel(labels, "tracebag.restart.enabled", "true"))
        {
            throw new TracebagException(StatusCodes.Status403Forbidden, "restart_not_allowed", "Restart is not enabled for the requested container.");
        }
    }

    public string GetDotnetTmpVolume(ContainerListResponse container)
    {
        var labels = container.Labels ?? new Dictionary<string, string>();
        if (!labels.TryGetValue("tracebag.dotnet.tmpVolume", out var volume) || string.IsNullOrWhiteSpace(volume))
        {
            throw new TracebagException(
                StatusCodes.Status400BadRequest,
                "dotnet_tmp_volume_missing",
                "The target container has no configured shared /tmp diagnostics volume.");
        }

        return volume;
    }

    public ContainerDto ToDto(ContainerListResponse container)
    {
        var labels = container.Labels ?? new Dictionary<string, string>();
        var name = GetContainerName(container);
        var identity = _identityResolver.Resolve(container);
        return new ContainerDto(
            identity.Id,
            container.ID,
            identity.Source,
            name,
            GetServiceName(labels),
            identity.ComposeProject,
            container.Image,
            container.Status ?? string.Empty,
            container.State ?? string.Empty,
            GetKind(labels),
            GetDisplayName(labels, name),
            new DateTimeOffset(DateTime.SpecifyKind(container.Created, DateTimeKind.Utc)),
            _options.RestartEnabled && HasLabel(labels, "tracebag.restart.enabled", "true"));
    }

    public ContainerIdentity GetIdentity(ContainerListResponse container)
    {
        return _identityResolver.Resolve(container);
    }

    public bool IsAllowedEvent(
        string dockerId,
        string containerName,
        IDictionary<string, string> labels)
    {
        return HasLabel(labels, _options.AllowedLabelKey, _options.AllowedLabelValue)
            && MatchesEnvironment(labels)
            && !IsBlocked(dockerId, labels, containerName, GetServiceName(labels));
    }

    public string GetDisplayName(IDictionary<string, string>? labels, string fallback)
    {
        if (labels is not null
            && labels.TryGetValue("tracebag.displayName", out var displayName)
            && !string.IsNullOrWhiteSpace(displayName))
        {
            return displayName;
        }

        return fallback;
    }

    public string GetKind(IDictionary<string, string>? labels)
    {
        return labels is not null && labels.TryGetValue("tracebag.kind", out var kind)
            ? kind
            : "container";
    }

    public string GetContainerName(ContainerListResponse container)
    {
        return container.Names?.FirstOrDefault()?.TrimStart('/') ?? container.ID[..Math.Min(12, container.ID.Length)];
    }

    public string GetServiceName(IDictionary<string, string>? labels)
    {
        return labels is not null && labels.TryGetValue("com.docker.compose.service", out var serviceName)
            ? serviceName
            : string.Empty;
    }

    private static bool IsBlocked(string containerId, IDictionary<string, string> labels, string name, string serviceName)
    {
        if (HasLabel(labels, "tracebag.internal", "true")
            || HasLabel(labels, "tracebag.runner", "true"))
        {
            return true;
        }

        if (name.StartsWith("tracebag-runner-", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("tracebag-recording-runner-", StringComparison.OrdinalIgnoreCase)
            || string.Equals(serviceName, "tracebag", StringComparison.OrdinalIgnoreCase)
            || string.Equals(serviceName, "tracebag-postgres", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(serviceName, "postgres", StringComparison.OrdinalIgnoreCase)
            && !HasLabel(labels, "tracebag.databaseAllowed", "true");
    }

    private bool MatchesEnvironment(IDictionary<string, string> labels)
    {
        return string.IsNullOrWhiteSpace(_options.EnvironmentLabelKey)
            || HasLabel(labels, _options.EnvironmentLabelKey, _options.EnvironmentLabelValue ?? string.Empty);
    }

    private static bool HasLabel(IDictionary<string, string> labels, string key, string expected)
    {
        return labels.TryGetValue(key, out var actual)
            && string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
    }
}
