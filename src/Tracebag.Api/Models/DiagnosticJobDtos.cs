namespace Tracebag.Api.Models;

public sealed record DiagnosticJobCreateRequest(
    int ProcessId,
    string Profile,
    int? DurationSeconds,
    string? Confirmation);

public sealed record DiagnosticJobResponse(
    string Id,
    string ContainerId,
    string ContainerName,
    string Profile,
    string Status,
    int Progress,
    string? StatusMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset DeadlineAt,
    string CreatedBy,
    int ProcessId,
    int RuntimeMajor,
    string RunnerImage,
    string ToolVersion,
    string? ArtifactId,
    object Inputs,
    object? Outcome,
    string? ErrorCode,
    string? ErrorMessage);

public sealed record DiagnosticJobEventResponse(
    long Id,
    string JobId,
    DateTimeOffset Timestamp,
    string Type,
    string Status,
    int Progress,
    string Message,
    object? Metadata);

public sealed record DiagnosticJobProfileResponse(
    string Id,
    string DisplayName,
    string Description,
    int DefaultDurationSeconds,
    int MaxDurationSeconds,
    bool Sensitive,
    bool Enabled);
