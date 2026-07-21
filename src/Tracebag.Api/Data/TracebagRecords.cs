using NpgsqlTypes;

namespace Tracebag.Api.Data;

public sealed class ContainerTargetRecord
{
    public string Id { get; set; } = string.Empty;
    public string IdentitySource { get; set; } = string.Empty;
    public string? CurrentDockerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? ComposeProject { get; set; }
    public string? ComposeService { get; set; }
    public string? ComposeReplica { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Image { get; set; } = string.Empty;
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public bool Active { get; set; }
}

public sealed class ContainerInstanceRecord
{
    public string DockerId { get; set; } = string.Empty;
    public string ContainerTargetId { get; set; } = string.Empty;
    public ContainerTargetRecord? ContainerTarget { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Image { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public DateTimeOffset? RemovedAt { get; set; }
}

public sealed class DockerEventRecord
{
    public long Id { get; set; }
    public string ContainerTargetId { get; set; } = string.Empty;
    public string DockerId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public string? AttributesJson { get; set; }
}

public sealed class ArtifactRecord
{
    public string Id { get; set; } = string.Empty;
    public string ContainerId { get; set; } = string.Empty;
    public string ContainerName { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public long Size { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public string? DiagnosticJobId { get; set; }
    public string? Sha256 { get; set; }
    public string? ManifestFileName { get; set; }
    public string State { get; set; } = "available";
}

public sealed class DiagnosticJobRecord
{
    public string Id { get; set; } = string.Empty;
    public string ContainerId { get; set; } = string.Empty;
    public string ContainerName { get; set; } = string.Empty;
    public string DockerId { get; set; } = string.Empty;
    public int ProcessId { get; set; }
    public string Profile { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int Progress { get; set; }
    public string? StatusMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset DeadlineAt { get; set; }
    public DateTimeOffset? CancelRequestedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string? IdempotencyKey { get; set; }
    public string RequestFingerprint { get; set; } = string.Empty;
    public string InputsJson { get; set; } = "{}";
    public string? OutcomeJson { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? RunnerContainerId { get; set; }
    public int RuntimeMajor { get; set; }
    public string RunnerImage { get; set; } = string.Empty;
    public string ToolVersion { get; set; } = string.Empty;
    public string? ArtifactId { get; set; }
}

public sealed class DiagnosticJobEventRecord
{
    public long Id { get; set; }
    public string JobId { get; set; } = string.Empty;
    public DiagnosticJobRecord? Job { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int Progress { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? MetadataJson { get; set; }
}

public sealed class AuditEventRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset Timestamp { get; set; }
    public string User { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? TargetContainerId { get; set; }
    public string? TargetContainerName { get; set; }
    public string Result { get; set; } = string.Empty;
    public string? MetadataJson { get; set; }
}

public sealed class LogStreamRecord
{
    public long Id { get; set; }
    public string ContainerId { get; set; } = string.Empty;
    public string CurrentDockerId { get; set; } = string.Empty;
    public string ContainerName { get; set; } = string.Empty;
    public string? Image { get; set; }
    public string Parser { get; set; } = "auto";
    public int RetentionDays { get; set; }
    public long MaxBytes { get; set; }
    public bool Active { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    public string? LabelsJson { get; set; }
}

public sealed class LogEntryRecord
{
    public long Id { get; set; }
    public long LogStreamId { get; set; }
    public LogStreamRecord? LogStream { get; set; }
    public string ContainerId { get; set; } = string.Empty;
    public string DockerId { get; set; } = string.Empty;
    public DateTimeOffset ReceivedAt { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public string SourceTimestamp { get; set; } = string.Empty;
    public string Stream { get; set; } = string.Empty;
    public string Line { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Level { get; set; }
    public string? ExceptionType { get; set; }
    public string? TraceId { get; set; }
    public string? ParsedJson { get; set; }
    public string Fingerprint { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public NpgsqlTsVector SearchVector { get; set; } = null!;
}

public sealed class LogCheckpointRecord
{
    public long Id { get; set; }
    public string ContainerId { get; set; } = string.Empty;
    public string DockerId { get; set; } = string.Empty;
    public DateTimeOffset? LastTimestamp { get; set; }
    public string? LastFingerprint { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class CounterRecordingSessionRecord
{
    public string Id { get; set; } = string.Empty;
    public string ContainerId { get; set; } = string.Empty;
    public string ContainerName { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Notes { get; set; }
    public string? RunnerContainerId { get; set; }
    public int ProcessId { get; set; }
    public string Preset { get; set; } = string.Empty;
    public int IntervalSeconds { get; set; }
    public int MaxDurationSeconds { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? StoppedAt { get; set; }
    public DateTimeOffset? LastSampleAt { get; set; }
    public long SampleCount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public string? StopReason { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ProvidersJson { get; set; }
    public int RuntimeMajor { get; set; } = 8;
    public string RunnerImage { get; set; } = string.Empty;
    public string ToolVersion { get; set; } = string.Empty;
}

public sealed class CounterSampleRecord
{
    public long Id { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public CounterRecordingSessionRecord? Session { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string CounterType { get; set; } = string.Empty;
    public double Value { get; set; }
    public string? TagsJson { get; set; }
}

public sealed class CounterRollup1mRecord
{
    public long Id { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public CounterRecordingSessionRecord? Session { get; set; }
    public string ContainerId { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string CounterType { get; set; } = string.Empty;
    public DateTimeOffset BucketStart { get; set; }
    public double Average { get; set; }
    public double Minimum { get; set; }
    public double Maximum { get; set; }
    public int Count { get; set; }
}

public sealed class IncidentRecord
{
    public string Id { get; set; } = string.Empty;
    public string ContainerId { get; set; } = string.Empty;
    public string ContainerName { get; set; } = string.Empty;
    public string DockerId { get; set; } = string.Empty;
    public int ProcessId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Profile { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public string? Notes { get; set; }
    public string Status { get; set; } = string.Empty;
    public int Progress { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset WindowStart { get; set; }
    public DateTimeOffset? WindowEnd { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public string CaptureOptionsJson { get; set; } = "{}";
}

public sealed class IncidentTimelineRecord
{
    public long Id { get; set; }
    public string IncidentId { get; set; } = string.Empty;
    public IncidentRecord? Incident { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string? EvidenceId { get; set; }
    public string? MetadataJson { get; set; }
}

public sealed class IncidentEvidenceRecord
{
    public string Id { get; set; } = string.Empty;
    public string IncidentId { get; set; } = string.Empty;
    public IncidentRecord? Incident { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTimeOffset CapturedAt { get; set; }
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
    public string? SourceId { get; set; }
    public string? ArtifactId { get; set; }
    public string SummaryJson { get; set; } = "{}";
    public string PayloadJson { get; set; } = "{}";
    public bool SelectedByDefault { get; set; }
    public bool Sensitive { get; set; }
    public string RedactionStatus { get; set; } = string.Empty;
}

public sealed class IncidentFindingRecord
{
    public string Id { get; set; } = string.Empty;
    public string IncidentId { get; set; } = string.Empty;
    public IncidentRecord? Incident { get; set; }
    public string? AnalysisRunId { get; set; }
    public AnalysisRunRecord? AnalysisRun { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Confidence { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class AnalysisRunRecord
{
    public string Id { get; set; } = string.Empty;
    public string IncidentId { get; set; } = string.Empty;
    public IncidentRecord? Incident { get; set; }
    public int EnvelopeVersion { get; set; }
    public string AnalyzerVersion { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public string? EnvelopeJson { get; set; }
}

public sealed class IncidentFindingEvidenceRecord
{
    public string FindingId { get; set; } = string.Empty;
    public IncidentFindingRecord? Finding { get; set; }
    public string EvidenceId { get; set; } = string.Empty;
    public IncidentEvidenceRecord? Evidence { get; set; }
}
