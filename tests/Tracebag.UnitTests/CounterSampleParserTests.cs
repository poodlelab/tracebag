using Tracebag.Api.Diagnostics;
using System.Globalization;

namespace Tracebag.UnitTests;

public sealed class CounterSampleParserTests
{
    [Fact]
    public void ParsesQuotedCounterCsvLine()
    {
        var parser = new CounterSampleParser();

        var parsed = parser.TryParse(
            "\"2026-06-28T21:00:05Z\",\"System.Runtime\",\"ThreadPool Queue Length\",\"Metric\",\"42\"",
            out var sample);

        Assert.True(parsed);
        Assert.Equal("System.Runtime", sample.Provider);
        Assert.Equal("ThreadPool Queue Length", sample.Name);
        Assert.Equal("Metric", sample.CounterType);
        Assert.Equal(42, sample.Value);
    }

    [Fact]
    public void IgnoresHeaderAndInvalidValues()
    {
        var parser = new CounterSampleParser();

        Assert.False(parser.TryParse("Timestamp,Provider,Name,CounterType,Value", out _));
        Assert.False(parser.TryParse("2026-06-28T21:00:05Z,System.Runtime,cpu-usage,Metric,not-a-number", out _));
    }

    [Fact]
    public void LiveMetricUsesTheSameNormalizedParser()
    {
        var parser = new CounterSampleParser();
        var receivedAt = DateTimeOffset.Parse("2026-06-28T21:00:06Z", CultureInfo.InvariantCulture);

        var parsed = parser.TryParseMetric(
            "2026-06-28T21:00:05Z,System.Runtime,cpu-usage,Metric,12.5",
            receivedAt,
            out var metric);

        Assert.True(parsed);
        Assert.Equal("System.Runtime|cpu-usage|Metric", metric.Id);
        Assert.Equal(12.5, metric.Value);
        Assert.Equal(receivedAt, metric.ReceivedAt);
    }
}
