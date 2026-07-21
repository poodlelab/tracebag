namespace Tracebag.Api.Diagnostics;

public sealed record DiagnosticSession(
    string SessionId,
    string TargetContainerId,
    string TargetContainerName,
    string RunnerContainerId,
    string RunnerContainerName,
    DateTimeOffset StartedAt,
    string User);
