namespace Tracebag.Api.Models;

public sealed record CounterRecordingStartRequest(
    int ProcessId,
    string Preset,
    int? IntervalSeconds,
    int? MaxDurationMinutes,
    string? Name);

public sealed record CounterRecordingUpdateRequest(string? Name, string? Notes);

public sealed record CounterRecordingResponse(
    string Id,
    string ContainerId,
    string ContainerName,
    int ProcessId,
    string Preset,
    int IntervalSeconds,
    DateTimeOffset StartedAt,
    DateTimeOffset? StoppedAt,
    DateTimeOffset? LastSampleAt,
    string Status,
    long SampleCount,
    string CreatedBy,
    string? Name,
    string? StopReason,
    string? ErrorMessage,
    string? Notes,
    int RuntimeMajor,
    string RunnerImage,
    string ToolVersion);

public sealed record CounterRecordingStartResponse(string Id, string Status);

public sealed record CounterRecordingDetailResponse(
    CounterRecordingResponse Recording,
    IReadOnlyList<CounterSeriesDescriptorDto> Series);

public sealed record CounterSeriesDescriptorDto(
    string Provider,
    string Name,
    string CounterType,
    long SampleCount,
    DateTimeOffset? FirstSampleAt,
    DateTimeOffset? LastSampleAt);

public sealed record CounterRecordingSamplesResponse(
    string RecordingId,
    string Resolution,
    IReadOnlyList<string> AvailableResolutions,
    bool Truncated,
    IReadOnlyList<CounterSeriesDto> Series);

public sealed record CounterSeriesDto(
    string Provider,
    string Name,
    string CounterType,
    CounterSeriesSummaryDto Summary,
    IReadOnlyList<CounterSamplePointDto> Points);

public sealed record CounterSeriesSummaryDto(
    double Minimum,
    double Maximum,
    double Average,
    DateTimeOffset PeakAt,
    long SampleCount);

public sealed record CounterSamplePointDto(
    DateTimeOffset Timestamp,
    double Value,
    double Minimum,
    double Maximum,
    int Count);
