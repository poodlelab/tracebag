using Microsoft.AspNetCore.Mvc;
using Tracebag.Api.Logs;
using Tracebag.Api.Models;

namespace Tracebag.Api.Controllers;

[ApiController]
[Route("api/logs")]
public sealed class LogsController(
    LogStore logStore,
    LogIngestionCoordinator coordinator) : ControllerBase
{
    [HttpGet("search")]
    public async Task<LogSearchResponse> Search(
        [FromQuery] string? containerId,
        [FromQuery] LogSearchRequest request,
        CancellationToken cancellationToken)
    {
        return await logStore.SearchAsync(containerId, request, cancellationToken);
    }

    [HttpGet("status")]
    public async Task<LogIngestionStatusDto> Status(CancellationToken cancellationToken)
    {
        return await coordinator.StatusAsync(cancellationToken);
    }
}
