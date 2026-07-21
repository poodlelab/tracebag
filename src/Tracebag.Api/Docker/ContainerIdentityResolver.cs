using System.Security.Cryptography;
using System.Text;
using Docker.DotNet.Models;

namespace Tracebag.Api.Docker;

public sealed class ContainerIdentityResolver
{
    public ContainerIdentity Resolve(ContainerListResponse container)
    {
        var labels = container.Labels ?? new Dictionary<string, string>();
        var name = container.Names?.FirstOrDefault()?.TrimStart('/') ?? string.Empty;
        return Resolve(container.ID, name, labels);
    }

    public ContainerIdentity Resolve(
        string dockerId,
        string? containerName,
        IDictionary<string, string> labels)
    {
        if (labels.TryGetValue("tracebag.identity", out var explicitIdentity)
            && !string.IsNullOrWhiteSpace(explicitIdentity))
        {
            return new ContainerIdentity(
                BuildKey("custom", explicitIdentity),
                "explicit-label",
                null,
                null,
                null);
        }

        labels.TryGetValue("com.docker.compose.project", out var project);
        labels.TryGetValue("com.docker.compose.service", out var service);
        labels.TryGetValue("com.docker.compose.container-number", out var replica);
        if (!string.IsNullOrWhiteSpace(project) && !string.IsNullOrWhiteSpace(service))
        {
            var normalizedReplica = string.IsNullOrWhiteSpace(replica) ? "1" : replica;
            return new ContainerIdentity(
                BuildKey("compose", project, service, normalizedReplica),
                "compose",
                project,
                service,
                normalizedReplica);
        }

        if (!string.IsNullOrWhiteSpace(containerName))
        {
            return new ContainerIdentity(
                BuildKey("name", containerName),
                "container-name",
                null,
                null,
                null);
        }

        return new ContainerIdentity(
            BuildKey("docker", dockerId),
            "docker-id",
            null,
            null,
            null);
    }

    private static string BuildKey(string prefix, params string[] parts)
    {
        var normalized = string.Join(':', parts.Select(NormalizePart));
        var key = $"{prefix}:{normalized}";
        if (key.Length <= 128)
        {
            return key;
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16].ToLowerInvariant();
        return $"{key[..111]}:{hash}";
    }

    private static string NormalizePart(string value)
    {
        var normalized = string.Concat(value.Trim().ToLowerInvariant().Select(character =>
            char.IsLetterOrDigit(character) || character is '.' or '_' or '-'
                ? character
                : '-')).Trim('-');
        return string.IsNullOrWhiteSpace(normalized) ? "unknown" : normalized;
    }
}

public sealed record ContainerIdentity(
    string Id,
    string Source,
    string? ComposeProject,
    string? ComposeService,
    string? ComposeReplica);
