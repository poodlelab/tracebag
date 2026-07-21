using Tracebag.Api.Models;
using Tracebag.Api.Security;

namespace Tracebag.Api.Incidents;

public sealed class GuidedIncidentProfileCatalog
{
    private static readonly Dictionary<string, GuidedIncidentProfileDto> Profiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["frozen-api"] = new("frozen-api", "Frozen API", "Capture thread-pool pressure and managed stacks for an unresponsive API.", "thread-pool", "stack-snapshot", "threading-trace", 30),
        ["high-cpu"] = new("high-cpu", "High CPU", "Correlate container CPU, runtime counters, logs and sampled CPU hot paths.", "runtime", "stack-snapshot", "cpu-trace", 30),
        ["high-memory"] = new("high-memory", "High Memory", "Inspect memory pressure, GC counters and a managed heap graph.", "gc-pressure", "gc-dump", null, 30),
        ["request-timeouts"] = new("request-timeouts", "Request Timeouts", "Correlate request counters, timeout logs and scheduling evidence.", "request-pressure", "stack-snapshot", "threading-trace", 30),
        ["lock-contention"] = new("lock-contention", "Lock Contention", "Capture runtime contention counters, stacks and optional contention events.", "contention", "stack-snapshot", "contention-trace", 30)
    };

    public IReadOnlyList<GuidedIncidentProfileDto> List() => Profiles.Values.OrderBy(x => x.DisplayName).ToArray();

    public GuidedIncidentProfileDto Get(string? id) =>
        !string.IsNullOrWhiteSpace(id) && Profiles.TryGetValue(id, out var profile)
            ? profile
            : throw new TracebagException(400, "incident_profile_invalid", "Choose one of the supported guided incident profiles.");
}
