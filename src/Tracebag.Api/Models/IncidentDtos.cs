namespace Tracebag.Api.Models;

public sealed record IncidentCreateRequest(
    int ProcessId,
    string Profile,
    string? Title,
    string? Reason,
    int? CaptureSeconds,
    bool IncludeTrace = false);

public sealed record IncidentUpdateRequest(string? Notes, string? Status);

public sealed record GuidedIncidentProfileDto(
    string Id,
    string DisplayName,
    string Description,
    string CounterPreset,
    string PrimaryDiagnostic,
    string? OptionalTrace,
    int DefaultCaptureSeconds);

public sealed record IncidentSummaryDto(
    string Id,
    string ContainerId,
    string ContainerName,
    int ProcessId,
    string Title,
    string Profile,
    string? Reason,
    string? Notes,
    string Status,
    int Progress,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset WindowStart,
    DateTimeOffset? WindowEnd,
    DateTimeOffset? CompletedAt,
    string? ErrorMessage);

public sealed record IncidentTimelineDto(
    long Id,
    DateTimeOffset Timestamp,
    string Type,
    string Severity,
    string Title,
    string Summary,
    string? EvidenceId,
    object? Metadata);

public sealed record IncidentEvidenceDto(
    string Id,
    string Kind,
    string Title,
    DateTimeOffset CapturedAt,
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? SourceId,
    string? ArtifactId,
    object Summary,
    object Payload,
    bool SelectedByDefault,
    bool Sensitive,
    string RedactionStatus);

public sealed record IncidentFindingDto(
    string Id,
    string Code,
    string Severity,
    string Confidence,
    string Title,
    string Summary,
    DateTimeOffset CreatedAt,
    IReadOnlyList<string> EvidenceIds);

public sealed record IncidentDetailDto(
    IncidentSummaryDto Incident,
    IReadOnlyList<IncidentTimelineDto> Timeline,
    IReadOnlyList<IncidentEvidenceDto> Evidence,
    IReadOnlyList<IncidentFindingDto> Findings,
    AnalysisRunDto? Analysis);

public sealed record IncidentDeleteResult(
    string IncidentId,
    string Status,
    int DeletedTimelineEvents,
    int DeletedEvidence,
    int DeletedFindings,
    int DeletedAnalysisRuns,
    int ReleasedDiagnosticJobs,
    int ReleasedRecordings,
    int ReleasedArtifacts);

public sealed record AnalysisRunDto(
    string Id,
    string IncidentId,
    int EnvelopeVersion,
    string AnalyzerVersion,
    string Status,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    string? ErrorMessage,
    AnalysisEnvelope? Envelope);

public sealed record AnalysisEnvelope(
    int SchemaVersion,
    string AnalyzerVersion,
    string IncidentId,
    DateTimeOffset GeneratedAt,
    AnalysisWindow Window,
    IReadOnlyList<AnalysisSource> Sources,
    IReadOnlyList<AnalysisComponent> Components,
    IReadOnlyList<AnalysisObservation> Observations,
    IReadOnlyList<AnalysisCorrelation> Correlations,
    IReadOnlyList<AnalysisLimitation> Limitations,
    AnalysisDisclosure Disclosure);

public sealed record AnalysisWindow(DateTimeOffset From, DateTimeOffset To);
public sealed record AnalysisSource(string EvidenceId, string Kind, string Title, string? ArtifactId);
public sealed record AnalysisComponent(string Name, string Status, long DurationMilliseconds, int ObservationCount, string? Error);
public sealed record AnalysisObservation(string Id, string Analyzer, string Code, string Severity, string Confidence, string Title, string Summary, IReadOnlyList<string> EvidenceIds, object? Data);
public sealed record AnalysisCorrelation(string Code, string Confidence, string Summary, IReadOnlyList<string> ObservationIds, IReadOnlyList<string> EvidenceIds);
public sealed record AnalysisLimitation(string Code, string Summary, string? EvidenceId);
public sealed record AnalysisDisclosure(bool LocalOnly, bool ExternalProvidersUsed, bool RawPayloadsIncluded);

public sealed record TracebagExportSelection(
    bool IncludePinnedLogs,
    IReadOnlyList<string> ArtifactIds,
    bool IncludeSensitiveArtifacts);
