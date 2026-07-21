using Microsoft.AspNetCore.Mvc;
using Tracebag.Api.Audit;
using Tracebag.Api.Analysis;
using Tracebag.Api.Incidents;
using Tracebag.Api.Models;

namespace Tracebag.Api.Controllers;

[ApiController]
[Route("api/incidents")]
public sealed class IncidentsController(IncidentService incidents, LocalAnalysisService analyses, TracebagExportService exports, AuditLog audit) : ControllerBase
{
    [HttpGet("profiles")]
    public IReadOnlyList<GuidedIncidentProfileDto> Profiles() => incidents.Profiles();

    [HttpGet]
    public Task<IReadOnlyList<IncidentSummaryDto>> List([FromQuery] string? containerId, [FromQuery] string? status, CancellationToken ct) => incidents.ListAsync(containerId, status, ct);

    [HttpGet("{incidentId}")]
    public Task<IncidentDetailDto> Get(string incidentId, CancellationToken ct) => incidents.GetAsync(incidentId, ct);

    [HttpPost("/api/containers/{containerId}/incidents")]
    public async Task<IActionResult> Create(string containerId, [FromBody] IncidentCreateRequest request, CancellationToken ct)
    {
        var result = await incidents.CreateAsync(containerId, request, CurrentUser(), ct);
        return AcceptedAtAction(nameof(Get), new { incidentId = result.Id }, result);
    }

    [HttpPatch("{incidentId}")]
    public Task<IncidentSummaryDto> Update(string incidentId, [FromBody] IncidentUpdateRequest request, CancellationToken ct) => incidents.UpdateAsync(incidentId, request, CurrentUser(), ct);

    [HttpDelete("{incidentId}")]
    public Task<IncidentDeleteResult> Delete(string incidentId, [FromQuery] string? confirm, CancellationToken ct) =>
        incidents.DeleteAsync(incidentId, confirm, CurrentUser(), ct);

    [HttpGet("{incidentId}/analysis")]
    public Task<AnalysisRunDto?> Analysis(string incidentId, CancellationToken ct) => analyses.LatestAsync(incidentId, ct);

    [HttpPost("{incidentId}/analysis")]
    public Task<AnalysisRunDto> Analyze(string incidentId, CancellationToken ct) => analyses.AnalyzeAsync(incidentId, CurrentUser(), ct);

    [HttpGet("{incidentId}/export")]
    public async Task Export(string incidentId, [FromQuery] bool includePinnedLogs = false, [FromQuery] string[]? artifactId = null, [FromQuery] bool includeSensitiveArtifacts = false, CancellationToken ct = default)
    {
        var detail = await incidents.GetAsync(incidentId, ct);
        Response.ContentType = "application/zip";
        Response.Headers.ContentDisposition = $"attachment; filename=tracebag-{incidentId}.zip";
        await exports.WriteAsync(incidentId, new TracebagExportSelection(includePinnedLogs, artifactId ?? [], includeSensitiveArtifacts), Response.Body, ct);
        await audit.WriteAsync(CurrentUser(), "incident.export", detail.Incident.ContainerId, detail.Incident.ContainerName, "success", new { incidentId, includePinnedLogs, artifactId, includeSensitiveArtifacts }, ct);
    }

    private string CurrentUser() => User.Identity?.Name ?? "anonymous";
}
