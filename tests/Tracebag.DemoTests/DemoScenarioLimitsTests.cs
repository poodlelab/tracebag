using Tracebag.Demo.Api.Scenarios;

namespace Tracebag.DemoTests;

public sealed class DemoScenarioLimitsTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    public void RejectsCpuDurationsOutsideHardBounds(int seconds)
    {
        Assert.Throws<DemoScenarioValidationException>(() => DemoScenarioLimits.CpuSeconds(seconds));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(33)]
    public void RejectsAllocationRatesOutsideHardBounds(int megabytesPerSecond)
    {
        Assert.Throws<DemoScenarioValidationException>(
            () => DemoScenarioLimits.AllocationMegabytesPerSecond(megabytesPerSecond));
    }

    [Theory]
    [InlineData(99)]
    [InlineData(5001)]
    public void RejectsSlowRequestsOutsideHardBounds(int milliseconds)
    {
        Assert.Throws<DemoScenarioValidationException>(() => DemoScenarioLimits.SlowMilliseconds(milliseconds));
    }

    [Fact]
    public void AcceptsDocumentedScenarioDefaults()
    {
        Assert.Equal(20, DemoScenarioLimits.CpuSeconds(20));
        Assert.Equal(2, DemoScenarioLimits.CpuWorkers(2));
        Assert.Equal(20, DemoScenarioLimits.AllocationMegabytesPerSecond(20));
        Assert.Equal(20, DemoScenarioLimits.ExceptionCount(20));
        Assert.Equal(3_000, DemoScenarioLimits.SlowMilliseconds(3_000));
        Assert.Equal(20, DemoScenarioLimits.ContentionWorkers(20));
        Assert.Equal(24, DemoScenarioLimits.StarvationWorkers(24));
    }
}
