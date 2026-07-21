namespace Tracebag.Api.Artifacts;

public sealed record ArtifactMetadata(
    string Id,
    string ContainerId,
    string ContainerName,
    string Type,
    string FileName,
    DateTimeOffset CreatedAt,
    long Size,
    string CreatedBy,
    DateTimeOffset ExpiresAt,
    string? DiagnosticJobId = null,
    string? Sha256 = null,
    string? ManifestFileName = null,
    string State = "available");

public sealed record ArtifactManifest(
    int SchemaVersion,
    string ArtifactId,
    string DiagnosticJobId,
    string ContainerId,
    string ContainerName,
    string Profile,
    string PayloadFileName,
    long Size,
    string Sha256,
    DateTimeOffset CreatedAt,
    string CreatedBy,
    int ProcessId,
    int RuntimeMajor,
    string RunnerImage,
    string ToolVersion,
    object Inputs,
    object Outcome);
