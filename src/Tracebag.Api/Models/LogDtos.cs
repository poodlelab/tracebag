namespace Tracebag.Api.Models;

public sealed record LogSearchRequest(
    string? Text,
    string? Level,
    string? Stream,
    bool ExceptionOnly = false,
    string? TraceId = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    string? Cursor = null,
    int Limit = 100);

public sealed record LogSearchResponse(
    IReadOnlyList<LogSearchEntryDto> Items,
    string? NextCursor,
    bool HasMore);

public sealed record LogSearchEntryDto(
    long Id,
    string ContainerId,
    string ContainerName,
    string DockerId,
    DateTimeOffset Timestamp,
    DateTimeOffset ReceivedAt,
    string Stream,
    string RawLine,
    string Message,
    string? Level,
    string? ExceptionType,
    string? TraceId,
    IReadOnlyDictionary<string, object?> Properties);

public sealed record LogIngestionStatusDto(
    string Status,
    int ActiveCollectors,
    int QueueDepth,
    int QueueCapacity,
    long PersistedEntries,
    long DroppedEntries,
    long DuplicateEntries,
    long RetentionDeletedEntries,
    long StoredEntries,
    long StoredBytes,
    DateTimeOffset? LastPersistedAt,
    DateTimeOffset? NewestLogTimestamp,
    double? IngestionLagSeconds,
    string? LastError);
