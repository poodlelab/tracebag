using Microsoft.AspNetCore.Mvc;
using Tracebag.Api.Docker;
using Tracebag.Api.Models;

namespace Tracebag.Api.Controllers;

[ApiController]
[Route("api/system")]
public sealed class SystemController(
    SystemStatusService systemStatusService,
    DockerEventCollector eventCollector) : ControllerBase
{
    [HttpGet("status")]
    public async Task<SystemStatusDto> GetStatus(CancellationToken cancellationToken)
    {
        return await systemStatusService.GetAsync(cancellationToken);
    }

    [HttpGet("events")]
    public async Task<IReadOnlyList<DockerEventDto>> GetEvents(
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        return await eventCollector.ListAsync(null, limit, cancellationToken);
    }
}
