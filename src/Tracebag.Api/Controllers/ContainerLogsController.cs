using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Tracebag.Api.Docker;
using Tracebag.Api.Logs;
using Tracebag.Api.Models;

namespace Tracebag.Api.Controllers;

[ApiController]
[Route("api/containers/{containerId}/logs")]
public sealed class ContainerLogsController(
    ContainerCatalog containerCatalog,
    ContainerPolicy containerPolicy,
    LogStore logStore,
    LogLiveHub liveHub) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [HttpGet("search")]
    public async Task<LogSearchResponse> Search(
        string containerId,
        [FromQuery] LogSearchRequest request,
        CancellationToken cancellationToken)
    {
        var container = await containerCatalog.GetAllowedAsync(containerId, cancellationToken);
        var identity = containerPolicy.GetIdentity(container);
        return await logStore.SearchAsync(identity.Id, request, cancellationToken);
    }

    [HttpGet("live")]
    public async Task Live(
        string containerId,
        [FromQuery] long? afterId,
        CancellationToken cancellationToken)
    {
        var container = await containerCatalog.GetAllowedAsync(containerId, cancellationToken);
        var identity = containerPolicy.GetIdentity(container);
        var headerId = Request.Headers.TryGetValue("Last-Event-ID", out var header)
            && long.TryParse(header.FirstOrDefault(), out var parsed)
                ? parsed
                : 0;
        var lastSentId = Math.Max(afterId ?? 0, headerId);
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";
        Response.Headers.Append("X-Accel-Buffering", "no");
        Response.ContentType = "text/event-stream";

        using var subscription = liveHub.Subscribe(identity.Id);
        var replay = await logStore.ReplayAfterAsync(identity.Id, lastSentId, 1_000, cancellationToken);
        foreach (var entry in replay)
        {
            if (entry.Id > lastSentId)
            {
                await WriteEventAsync(entry, cancellationToken);
                lastSentId = entry.Id;
            }
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            var available = subscription.Reader.WaitToReadAsync(cancellationToken).AsTask();
            var heartbeat = Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);
            var completed = await Task.WhenAny(available, heartbeat);
            if (completed == heartbeat)
            {
                await Response.WriteAsync(": keep-alive\n\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
                continue;
            }

            if (!await available)
            {
                break;
            }

            while (subscription.Reader.TryRead(out var entry))
            {
                if (entry.Id <= lastSentId)
                {
                    continue;
                }

                await WriteEventAsync(entry, cancellationToken);
                lastSentId = entry.Id;
            }
        }
    }

    private async Task WriteEventAsync(LogSearchEntryDto entry, CancellationToken cancellationToken)
    {
        await Response.WriteAsync($"id: {entry.Id}\n", cancellationToken);
        await Response.WriteAsync("event: log\n", cancellationToken);
        await Response.WriteAsync($"data: {JsonSerializer.Serialize(entry, JsonOptions)}\n\n", cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);
    }
}
