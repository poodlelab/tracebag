using Microsoft.AspNetCore.Mvc;
using Tracebag.Api.Artifacts;
using Tracebag.Api.Audit;

namespace Tracebag.Api.Controllers;

[ApiController]
[Route("api/artifacts")]
public sealed class ArtifactsController : ControllerBase
{
    private readonly ArtifactStore _artifactStore;
    private readonly AuditLog _auditLog;

    public ArtifactsController(ArtifactStore artifactStore, AuditLog auditLog)
    {
        _artifactStore = artifactStore;
        _auditLog = auditLog;
    }

    [HttpGet]
    public async Task<IReadOnlyList<ArtifactMetadata>> Get(CancellationToken cancellationToken)
    {
        return await _artifactStore.ListAsync(cancellationToken);
    }

    [HttpGet("{artifactId}/download")]
    public async Task<IActionResult> Download(string artifactId, CancellationToken cancellationToken)
    {
        var (metadata, path) = await _artifactStore.GetForDownloadAsync(artifactId, cancellationToken);
        await _auditLog.WriteAsync(CurrentUser(), "artifact.download", metadata.ContainerId, metadata.ContainerName, "success", new
        {
            artifactId,
            metadata.Type
        }, cancellationToken);
        return PhysicalFile(path, "application/octet-stream", Path.GetFileName(metadata.FileName), enableRangeProcessing: true);
    }

    [HttpGet("{artifactId}/manifest")]
    public async Task<IActionResult> Manifest(string artifactId, CancellationToken cancellationToken)
    {
        var (metadata, path) = await _artifactStore.GetManifestAsync(artifactId, cancellationToken);
        await _auditLog.WriteAsync(CurrentUser(), "artifact.manifest.download", metadata.ContainerId, metadata.ContainerName, "success", new
        {
            artifactId,
            metadata.DiagnosticJobId
        }, cancellationToken);
        return PhysicalFile(path, "application/json", $"{artifactId}-manifest.json", enableRangeProcessing: true);
    }

    [HttpDelete("{artifactId}")]
    public async Task<IActionResult> Delete(string artifactId, CancellationToken cancellationToken)
    {
        var metadata = await _artifactStore.DeleteAsync(artifactId, cancellationToken);
        await _auditLog.WriteAsync(CurrentUser(), "artifact.delete", metadata.ContainerId, metadata.ContainerName, "success", new
        {
            artifactId,
            metadata.Type
        }, cancellationToken);
        return Ok(new { status = "deleted" });
    }

    private string CurrentUser()
    {
        return User.Identity?.Name ?? "anonymous";
    }
}
