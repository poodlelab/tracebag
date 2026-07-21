using System.Security.Cryptography;
using System.Text;

namespace Tracebag.Api.Logs;

public sealed record LogTarget(
    string ContainerId,
    string DockerId,
    string ContainerName,
    string Image,
    string Parser,
    int RetentionDays,
    long MaxBytes,
    DateTimeOffset CreatedAt);

public sealed record PendingLogEntry(
    LogTarget Target,
    DateTimeOffset ReceivedAt,
    DateTimeOffset DockerTimestamp,
    DateTimeOffset Timestamp,
    string SourceTimestamp,
    string Stream,
    string Line,
    string Message,
    string? Level,
    string? ExceptionType,
    string? TraceId,
    string? PropertiesJson,
    string Fingerprint,
    long SizeBytes)
{
    public static PendingLogEntry Create(
        LogTarget target,
        string stream,
        string rawLine,
        DateTimeOffset receivedAt,
        LogParserChain parserChain)
    {
        var normalized = LogTimestampNormalizer.Normalize(rawLine, receivedAt);
        var parsed = parserChain.Parse(target.Parser, normalized.Line);
        var timestamp = parsed.ApplicationTimestamp ?? normalized.Timestamp;
        var fingerprintInput = $"{target.DockerId}\n{stream}\n{normalized.SourceTimestamp}\n{normalized.Line}";
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprintInput))).ToLowerInvariant();
        return new PendingLogEntry(
            target,
            receivedAt,
            normalized.Timestamp,
            timestamp,
            normalized.SourceTimestamp,
            stream,
            normalized.Line,
            parsed.Message,
            parsed.Level,
            parsed.ExceptionType,
            parsed.TraceId,
            parsed.PropertiesJson,
            fingerprint,
            Encoding.UTF8.GetByteCount(normalized.Line));
    }
}
