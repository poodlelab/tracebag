using Microsoft.AspNetCore.Http;
using Tracebag.Api.Incidents;
using Tracebag.Api.Models;
using Tracebag.Api.Security;

namespace Tracebag.UnitTests;

public sealed class IncidentContractsTests
{
    [Fact]
    public void GuidedProfilesAreFixedAndCoverSeriousProblemWorkflows()
    {
        IReadOnlyList<GuidedIncidentProfileDto> profiles = new GuidedIncidentProfileCatalog().List();

        Assert.Equal(5, profiles.Count);
        Assert.Equal(["frozen-api", "high-cpu", "high-memory", "lock-contention", "request-timeouts"], profiles.Select(x => x.Id).Order().ToArray());
        Assert.All(profiles, profile =>
        {
            Assert.NotEmpty(profile.CounterPreset);
            Assert.NotEmpty(profile.PrimaryDiagnostic);
            Assert.InRange(profile.DefaultCaptureSeconds, 10, 120);
        });
    }

    [Fact]
    public void RejectsArbitraryGuidedProfile()
    {
        TracebagException exception = Assert.Throws<TracebagException>(() => new GuidedIncidentProfileCatalog().Get("stack; curl attacker"));
        Assert.Equal("incident_profile_invalid", exception.Code);
    }

    [Fact]
    public void ExportRequiresExactArtifactSelectionAndSensitiveConfirmation()
    {
        IncidentEvidenceDto safe = Evidence("safe", "artifact-safe", false);
        IncidentEvidenceDto dump = Evidence("dump", "artifact-dump", true);

        Assert.Equal("incident_export_artifact_invalid", Assert.Throws<TracebagException>(() => TracebagBundlePolicy.Validate([safe, dump], new(false, ["other"], false))).Code);
        Assert.Equal("incident_export_sensitive_confirmation_required", Assert.Throws<TracebagException>(() => TracebagBundlePolicy.Validate([safe, dump], new(false, ["artifact-dump"], false))).Code);

        (HashSet<string>? Requested, IncidentEvidenceDto[]? Selected) = TracebagBundlePolicy.Validate([safe, dump], new(false, ["artifact-safe"], false));
        Assert.Single(Selected);
        Assert.Equal("artifact-safe", Selected[0].ArtifactId);
    }

    [Fact]
    public void FindingContractAlwaysNamesEvidence()
    {
        var finding = new IncidentFindingDto("finding", "thread-pool-starvation", "warning", "medium", "Starvation", "Queue and stacks correlate.", DateTimeOffset.UtcNow, ["evidence-counters", "evidence-stacks"]);

        Assert.NotEmpty(finding.EvidenceIds);
        Assert.All(finding.EvidenceIds, Assert.NotEmpty);
    }

    [Fact]
    public void IncidentCapacityStopsCreationAtTheConfiguredCeiling()
    {
        IncidentService.EnsureCapacity(9, 10);

        var exception = Assert.Throws<TracebagException>(() => IncidentService.EnsureCapacity(10, 10));

        Assert.Equal("incident_capacity_reached", exception.Code);
        Assert.Equal(StatusCodes.Status409Conflict, exception.StatusCode);
    }

    private static IncidentEvidenceDto Evidence(string id, string artifactId, bool sensitive) =>
        new(id, "diagnostic-artifact", id, DateTimeOffset.UtcNow, null, null, "job", artifactId, new { }, new { }, false, sensitive, sensitive ? "not-redacted-sensitive" : "not-redacted");
}
