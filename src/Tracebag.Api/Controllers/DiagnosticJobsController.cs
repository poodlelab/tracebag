using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Tracebag.Api.Diagnostics;
using Tracebag.Api.Models;

namespace Tracebag.Api.Controllers;

[ApiController]
[Route("api/diagnostic-jobs")]
public sealed class DiagnosticJobsController(DiagnosticJobService jobs) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [HttpGet("profiles")]
    public IReadOnlyList<DiagnosticJobProfileResponse> Profiles() => jobs.ListProfiles();

    [HttpGet]
    public Task<IReadOnlyList<DiagnosticJobResponse>> List(
        [FromQuery] string? containerId,
        [FromQuery] string? status,
        CancellationToken cancellationToken) => jobs.ListAsync(containerId, status, cancellationToken);

    [HttpGet("{jobId}")]
    public Task<DiagnosticJobResponse> Get(string jobId, CancellationToken cancellationToken) => jobs.GetAsync(jobId, cancellationToken);

    [HttpPost("/api/containers/{containerId}/diagnostic-jobs")]
    public async Task<IActionResult> Create(
        string containerId,
        [FromBody] DiagnosticJobCreateRequest request,
        CancellationToken cancellationToken)
    {
        var response = await jobs.CreateAsync(containerId, request, Request.Headers["Idempotency-Key"].FirstOrDefault(), CurrentUser(), cancellationToken);
        return AcceptedAtAction(nameof(Get), new { jobId = response.Id }, response);
    }

    [HttpPost("{jobId}/cancel")]
    public Task<DiagnosticJobResponse> Cancel(string jobId, CancellationToken cancellationToken) => jobs.CancelAsync(jobId, CurrentUser(), cancellationToken);

    [HttpGet("{jobId}/events")]
    public async Task Events(string jobId, [FromQuery] long afterId = 0, CancellationToken cancellationToken = default)
    {
        if (Request.Headers.TryGetValue("Last-Event-ID", out var lastEventId) && long.TryParse(lastEventId.FirstOrDefault(), out var parsed))
        {
            afterId = Math.Max(afterId, parsed);
        }
        _ = await jobs.GetAsync(jobId, cancellationToken);
        Response.Headers.CacheControl = "no-cache, no-transform";
        Response.Headers.Connection = "keep-alive";
        Response.Headers["X-Accel-Buffering"] = "no";
        Response.ContentType = "text/event-stream";
        var lastHeartbeat = DateTimeOffset.UtcNow;
        while (!cancellationToken.IsCancellationRequested)
        {
            var events = await jobs.GetEventsAsync(jobId, afterId, cancellationToken);
            foreach (var item in events)
            {
                await Response.WriteAsync($"id: {item.Id}\n", cancellationToken);
                await Response.WriteAsync($"event: {item.Type}\n", cancellationToken);
                await Response.WriteAsync($"data: {JsonSerializer.Serialize(item, JsonOptions)}\n\n", cancellationToken);
                afterId = item.Id;
            }
            if (events.Count > 0)
            {
                await Response.Body.FlushAsync(cancellationToken);
            }

            var current = await jobs.GetAsync(jobId, cancellationToken);
            if (DiagnosticJobStore.IsTerminal(current.Status) && events.Count == 0)
            {
                return;
            }

            if (DateTimeOffset.UtcNow - lastHeartbeat >= TimeSpan.FromSeconds(15))
            {
                await Response.WriteAsync(": keep-alive\n\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
                lastHeartbeat = DateTimeOffset.UtcNow;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }
    }

    private string CurrentUser() => User.Identity?.Name ?? "anonymous";
}
