using Tracebag.Api.Diagnostics;
using Tracebag.Api.Security;

namespace Tracebag.UnitTests;

public sealed class CounterPresetCatalogTests
{
    [Fact]
    public void RuntimePresetReturnsOnlyRuntimeProvider()
    {
        var catalog = new CounterPresetCatalog();

        var providers = catalog.GetProviders("runtime");

        Assert.Equal(["System.Runtime"], providers);
    }

    [Fact]
    public void UnknownPresetIsRejected()
    {
        var catalog = new CounterPresetCatalog();

        var ex = Assert.Throws<TracebagException>(() => catalog.GetProviders("custom-command"));

        Assert.Equal("counter_preset_invalid", ex.Code);
    }

    [Fact]
    public void CuratedPressurePresetsCannotSupplyCommands()
    {
        var catalog = new CounterPresetCatalog();

        Assert.Contains("threadpool-queue-length", string.Join(',', catalog.GetProviders("thread-pool")));
        Assert.Contains("alloc-rate", string.Join(',', catalog.GetProviders("gc-pressure")));
        Assert.Contains("current-requests", string.Join(',', catalog.GetProviders("request-pressure")));
        Assert.Contains("monitor-lock-contention-count", string.Join(',', catalog.GetProviders("contention")));
        Assert.Contains(catalog.List(), preset => preset.Id == "kestrel");
    }
}
