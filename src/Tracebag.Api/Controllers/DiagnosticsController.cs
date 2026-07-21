using System.Text.Json;
using Tracebag.Api.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Tracebag.Api.Controllers;

[ApiController]
[Route("api/diagnostics/sessions")]
public sealed class DiagnosticsController : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly DiagnosticRunnerService _diagnosticRunnerService;

    public DiagnosticsController(DiagnosticRunnerService diagnosticRunnerService)
    {
        _diagnosticRunnerService = diagnosticRunnerService;
    }

    [HttpGet("{sessionId}/stream")]
    public async Task Stream(string sessionId, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";
        Response.ContentType = "text/event-stream";

        await foreach (var item in _diagnosticRunnerService.StreamSessionAsync(sessionId, cancellationToken).WithCancellation(cancellationToken))
        {
            var eventName = item.Metric is not null ? "counter" : "log";
            var payload = (object?)item.Metric ?? item.Output!;
            await Response.WriteAsync($"event: {eventName}\n", cancellationToken);
            await Response.WriteAsync($"data: {JsonSerializer.Serialize(payload, JsonOptions)}\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }
    }

    [HttpDelete("{sessionId}")]
    public async Task<IActionResult> Stop(string sessionId, CancellationToken cancellationToken)
    {
        await _diagnosticRunnerService.StopSessionAsync(sessionId, User.Identity?.Name ?? "anonymous", cancellationToken);
        return Ok(new { status = "stopped" });
    }
}
