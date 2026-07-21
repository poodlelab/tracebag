using Tracebag.Api.Diagnostics;
using Tracebag.Api.Models;
using Microsoft.AspNetCore.Mvc;

namespace Tracebag.Api.Controllers;

[ApiController]
[Route("api/dotnet/recordings")]
public sealed class CounterRecordingsController : ControllerBase
{
    private readonly CounterRecordingService _recordingService;

    public CounterRecordingsController(CounterRecordingService recordingService)
    {
        _recordingService = recordingService;
    }

    [HttpGet]
    public async Task<IReadOnlyList<CounterRecordingResponse>> List(
        [FromQuery] string? status,
        [FromQuery] string? containerId,
        CancellationToken cancellationToken)
    {
        return await _recordingService.ListAsync(status, containerId, cancellationToken);
    }

    [HttpGet("{recordingId}")]
    public async Task<CounterRecordingDetailResponse> Get(string recordingId, CancellationToken cancellationToken)
    {
        return await _recordingService.GetDetailAsync(recordingId, cancellationToken);
    }

    [HttpGet("{recordingId}/samples")]
    public async Task<CounterRecordingSamplesResponse> Samples(
        string recordingId,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] string? resolution,
        CancellationToken cancellationToken)
    {
        return await _recordingService.GetSamplesAsync(recordingId, from, to, resolution, cancellationToken);
    }

    [HttpPost("{recordingId}/stop")]
    public async Task<CounterRecordingResponse> Stop(string recordingId, CancellationToken cancellationToken)
    {
        return await _recordingService.StopAsync(recordingId, CurrentUser(), cancellationToken);
    }

    [HttpPatch("{recordingId}")]
    public async Task<CounterRecordingResponse> Update(
        string recordingId,
        [FromBody] CounterRecordingUpdateRequest request,
        CancellationToken cancellationToken)
    {
        return await _recordingService.UpdateAsync(recordingId, request, CurrentUser(), cancellationToken);
    }

    [HttpGet("{recordingId}/export")]
    public async Task<IActionResult> Export(
        string recordingId,
        [FromQuery] string? format,
        CancellationToken cancellationToken)
    {
        var export = await _recordingService.ExportAsync(recordingId, format, cancellationToken);
        return File(export.Content, export.ContentType, export.FileName);
    }

    [HttpDelete("{recordingId}")]
    public async Task<IActionResult> Delete(
        string recordingId,
        [FromQuery] string? confirm,
        CancellationToken cancellationToken)
    {
        await _recordingService.DeleteAsync(recordingId, confirm, CurrentUser(), cancellationToken);
        return Ok(new { status = "deleted" });
    }

    private string CurrentUser()
    {
        return User.Identity?.Name ?? "anonymous";
    }
}
