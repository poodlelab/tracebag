using Tracebag.Api.Security;

namespace Tracebag.Api.Diagnostics;

public sealed class CounterPresetCatalog
{
    private static readonly Dictionary<string, CounterPreset> Presets =
        new Dictionary<string, CounterPreset>(StringComparer.OrdinalIgnoreCase)
        {
            ["runtime"] = new("runtime", "Runtime overview", ["System.Runtime"]),
            ["aspnet"] = new("aspnet", "ASP.NET Core", ["System.Runtime", "Microsoft.AspNetCore.Hosting"]),
            ["kestrel"] = new("kestrel", "Kestrel connections", ["Microsoft.AspNetCore.Server.Kestrel"]),
            ["thread-pool"] = new("thread-pool", "Thread-pool pressure", ["System.Runtime[threadpool-thread-count,threadpool-queue-length,threadpool-completed-items-count]"]),
            ["gc-pressure"] = new("gc-pressure", "GC and allocation pressure", ["System.Runtime[gc-heap-size,alloc-rate,gen-0-gc-count,gen-1-gc-count,gen-2-gc-count,time-in-gc,loh-size]"]),
            ["request-pressure"] = new("request-pressure", "Request pressure", ["Microsoft.AspNetCore.Hosting[requests-per-second,total-requests,current-requests,failed-requests]"]),
            ["contention"] = new("contention", "Runtime contention", ["System.Runtime[monitor-lock-contention-count,threadpool-queue-length,threadpool-thread-count]"]),
            ["all"] = new("all", "Full web profile", ["System.Runtime", "Microsoft.AspNetCore.Hosting", "Microsoft.AspNetCore.Server.Kestrel"])
        };

    public IReadOnlyList<string> GetProviders(string preset)
    {
        if (!Presets.TryGetValue(preset, out var definition))
        {
            throw new TracebagException(StatusCodes.Status400BadRequest, "counter_preset_invalid", "The requested counter preset is not allowed.");
        }

        return definition.Providers;
    }

    public IReadOnlyList<CounterPreset> List() => Presets.Values.OrderBy(preset => preset.Id).ToArray();
}

public sealed record CounterPreset(string Id, string DisplayName, IReadOnlyList<string> Providers);
