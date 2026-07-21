namespace Tracebag.Api.Models;

public sealed record ContainerDto(
    string Id,
    string DockerId,
    string IdentitySource,
    string Name,
    string ServiceName,
    string? ProjectName,
    string Image,
    string Status,
    string State,
    string Kind,
    string DisplayName,
    DateTimeOffset Created,
    bool RestartAllowed);
