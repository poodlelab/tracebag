using Tracebag.Api.Logs;
using System.Globalization;

namespace Tracebag.UnitTests;

public sealed class LogParserChainTests
{
    private readonly LogParserChain _parser = new();

    [Fact]
    public void ParsesGenericJsonWithoutLosingRawProperties()
    {
        var parsed = _parser.Parse(
            "auto",
            "{\"Timestamp\":\"2026-07-20T18:00:00Z\",\"LogLevel\":\"Information\",\"Message\":\"order accepted\",\"TraceId\":\"trace-12345678\",\"OrderId\":42}");

        Assert.Equal("order accepted", parsed.Message);
        Assert.Equal("information", parsed.Level);
        Assert.Equal("trace-12345678", parsed.TraceId);
        Assert.Contains("OrderId", parsed.PropertiesJson, StringComparison.Ordinal);
        Assert.Equal(DateTimeOffset.Parse("2026-07-20T18:00:00Z", CultureInfo.InvariantCulture), parsed.ApplicationTimestamp);
    }

    [Fact]
    public void ParsesSerilogCompactFieldsAndExceptionType()
    {
        var parsed = _parser.Parse(
            "serilog",
            "{\"@t\":\"2026-07-20T18:00:00Z\",\"@l\":\"Error\",\"@m\":\"request failed\",\"@x\":\"System.TimeoutException: timed out\"}");

        Assert.Equal("error", parsed.Level);
        Assert.Equal("request failed", parsed.Message);
        Assert.Equal("System.TimeoutException", parsed.ExceptionType);
    }

    [Fact]
    public void PlainParserDetectsLevelExceptionAndTrace()
    {
        var parsed = _parser.Parse("plain", "[ERR] System.InvalidOperationException traceId=abc12345678");

        Assert.Equal("error", parsed.Level);
        Assert.Equal("System.InvalidOperationException", parsed.ExceptionType);
        Assert.Equal("abc12345678", parsed.TraceId);
    }

    [Fact]
    public void FingerprintUsesExactDockerTimestampForResumeDeduplication()
    {
        var target = new LogTarget("target", "docker", "api", "image", "plain", 7, 1_000_000, DateTimeOffset.UtcNow);
        var first = PendingLogEntry.Create(target, "stdout", "2026-07-20T18:00:00.123456789Z same", DateTimeOffset.UtcNow, _parser);
        var replay = PendingLogEntry.Create(target, "stdout", "2026-07-20T18:00:00.123456789Z same", DateTimeOffset.UtcNow, _parser);
        var next = PendingLogEntry.Create(target, "stdout", "2026-07-20T18:00:00.223456789Z same", DateTimeOffset.UtcNow, _parser);

        Assert.Equal(first.Fingerprint, replay.Fingerprint);
        Assert.NotEqual(first.Fingerprint, next.Fingerprint);
        Assert.Equal("same", first.Line);
    }
}
