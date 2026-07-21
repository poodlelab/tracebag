using System.Text.Json;
using Docker.DotNet.Models;
using Microsoft.AspNetCore.Mvc;
using Tracebag.Api.Audit;
using Tracebag.Api.Diagnostics;
using Tracebag.Api.Docker;
using Tracebag.Api.Models;

namespace Tracebag.Api.Controllers;

[ApiController]
[Route("api/containers")]
public sealed class ContainersController : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ContainerCatalog _containerCatalog;
    private readonly ContainerPolicy _containerPolicy;
    private readonly DockerClientFactory _dockerClientFactory;
    private readonly DockerLogService _dockerLogService;
    private readonly DiagnosticRunnerService _diagnosticRunnerService;
    private readonly ContainerOperationalService _operationalService;
    private readonly DockerEventCollector _eventCollector;
    private readonly AuditLog _auditLog;

    public ContainersController(
        ContainerCatalog containerCatalog,
        ContainerPolicy containerPolicy,
        DockerClientFactory dockerClientFactory,
        DockerLogService dockerLogService,
        DiagnosticRunnerService diagnosticRunnerService,
        ContainerOperationalService operationalService,
        DockerEventCollector eventCollector,
        AuditLog auditLog)
    {
        _containerCatalog = containerCatalog;
        _containerPolicy = containerPolicy;
        _dockerClientFactory = dockerClientFactory;
        _dockerLogService = dockerLogService;
        _diagnosticRunnerService = diagnosticRunnerService;
        _operationalService = operationalService;
        _eventCollector = eventCollector;
        _auditLog = auditLog;
    }

    [HttpGet("{containerId}/overview")]
    public async Task<ContainerOverviewDto> GetOverview(
        string containerId,
        CancellationToken cancellationToken)
    {
        return await _operationalService.GetOverviewAsync(containerId, cancellationToken);
    }

    [HttpGet("{containerId}/events")]
    public async Task<IReadOnlyList<DockerEventDto>> GetEvents(
        string containerId,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var container = await _containerCatalog.GetAllowedAsync(containerId, cancellationToken);
        var identity = _containerPolicy.GetIdentity(container);
        return await _eventCollector.ListAsync(identity.Id, limit, cancellationToken);
    }

    [HttpGet]
    public async Task<IReadOnlyList<ContainerDto>> GetContainers(CancellationToken cancellationToken)
    {
        return await _containerCatalog.ListAllowedAsync(cancellationToken);
    }

    [HttpPost("{containerId}/restart")]
    public async Task<IActionResult> Restart(string containerId, CancellationToken cancellationToken)
    {
        var container = await _containerCatalog.GetAllowedAsync(containerId, cancellationToken);
        var identity = _containerPolicy.GetIdentity(container);
        _containerPolicy.EnsureRestartAllowed(container);
        await _dockerClientFactory.Client.Containers.RestartContainerAsync(
            container.ID,
            new ContainerRestartParameters { WaitBeforeKillSeconds = 10 },
            cancellationToken);
        await _auditLog.WriteAsync(CurrentUser(), "container.restart", identity.Id, _containerPolicy.GetContainerName(container), "success", new
        {
            dockerId = container.ID
        }, cancellationToken);
        return Ok(new { status = "restarted" });
    }

    [HttpGet("{containerId}/logs")]
    public async Task<IReadOnlyList<LogEventDto>> GetLogs(
        string containerId,
        [FromQuery] int tail = 300,
        [FromQuery] bool timestamps = true,
        CancellationToken cancellationToken = default)
    {
        return await _dockerLogService.GetTailAsync(containerId, tail, timestamps, cancellationToken);
    }

    [HttpGet("{containerId}/logs/stream")]
    public async Task StreamLogs(
        string containerId,
        [FromQuery] int tail = 100,
        [FromQuery] bool timestamps = true,
        CancellationToken cancellationToken = default)
    {
        await WriteSseAsync(_dockerLogService.StreamAsync(containerId, tail, timestamps, cancellationToken), cancellationToken);
    }

    [HttpGet("{containerId}/dotnet/processes")]
    public async Task<IReadOnlyList<DotnetProcessDto>> GetDotnetProcesses(string containerId, CancellationToken cancellationToken)
    {
        return await _diagnosticRunnerService.ListDotnetProcessesAsync(containerId, CurrentUser(), cancellationToken);
    }

    [HttpPost("{containerId}/dotnet/counters")]
    public async Task<CounterSessionResponse> StartCounters(
        string containerId,
        [FromBody] CounterSessionRequest request,
        CancellationToken cancellationToken)
    {
        return await _diagnosticRunnerService.StartCounterSessionAsync(containerId, request, CurrentUser(), cancellationToken);
    }

    private async Task WriteSseAsync(IAsyncEnumerable<LogEventDto> events, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";
        Response.ContentType = "text/event-stream";

        await foreach (var logEvent in events.WithCancellation(cancellationToken))
        {
            var json = JsonSerializer.Serialize(logEvent, JsonOptions);
            await Response.WriteAsync("event: log\n", cancellationToken);
            await Response.WriteAsync($"data: {json}\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }
    }

    private string CurrentUser()
    {
        return User.Identity?.Name ?? "anonymous";
    }
}
