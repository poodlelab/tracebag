using Tracebag.Api.Diagnostics;
using Tracebag.Api.Models;
using Microsoft.AspNetCore.Mvc;

namespace Tracebag.Api.Controllers;

[ApiController]
[Route("api/containers/{containerId}/dotnet/recordings")]
public sealed class ContainerCounterRecordingsController : ControllerBase
{
    private readonly CounterRecordingService _recordingService;

    public ContainerCounterRecordingsController(CounterRecordingService recordingService)
    {
        _recordingService = recordingService;
    }

    [HttpPost]
    public async Task<CounterRecordingStartResponse> Start(
        string containerId,
        [FromBody] CounterRecordingStartRequest request,
        CancellationToken cancellationToken)
    {
        return await _recordingService.StartAsync(containerId, request, CurrentUser(), cancellationToken);
    }

    private string CurrentUser()
    {
        return User.Identity?.Name ?? "anonymous";
    }
}
