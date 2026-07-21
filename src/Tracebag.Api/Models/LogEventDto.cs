namespace Tracebag.Api.Models;

public sealed record LogEventDto(string Stream, string Line, DateTimeOffset? Timestamp);
