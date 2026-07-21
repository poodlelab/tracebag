using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Docker.DotNet.Models;
using Tracebag.Api.Audit;
using Tracebag.Api.Auth;
using Tracebag.Api.Docker;
using Tracebag.Api.Models;
using Tracebag.Api.Security;

namespace Tracebag.Api.Diagnostics;

public sealed class DiagnosticRunnerService
{
    private static readonly Regex ProcessLinePattern = new(@"^\s*(?<pid>\d+)\s+(?<name>\S+)(?<cmd>.*)$", RegexOptions.Compiled);
    private readonly DockerClientFactory _dockerClientFactory;
    private readonly ContainerCatalog _containerCatalog;
    private readonly ContainerPolicy _containerPolicy;
    private readonly DockerLogService _dockerLogService;
    private readonly CounterPresetCatalog _counterPresetCatalog;
    private readonly DiagnosticRunnerCatalog _runnerCatalog;
    private readonly DiagnosticRunnerImageService _runnerImages;
    private readonly DiagnosticRunnerContainerPolicy _runnerPolicy;
    private readonly CounterSampleParser _counterSampleParser;
    private readonly DiagnosticSessionRegistry _sessionRegistry;
    private readonly AuditLog _auditLog;
    private readonly TracebagOptions _options;
    private readonly ILogger<DiagnosticRunnerService> _logger;

    public DiagnosticRunnerService(
        DockerClientFactory dockerClientFactory,
        ContainerCatalog containerCatalog,
        ContainerPolicy containerPolicy,
        DockerLogService dockerLogService,
        CounterPresetCatalog counterPresetCatalog,
        DiagnosticRunnerCatalog runnerCatalog,
        DiagnosticRunnerImageService runnerImages,
        DiagnosticRunnerContainerPolicy runnerPolicy,
        CounterSampleParser counterSampleParser,
        DiagnosticSessionRegistry sessionRegistry,
        AuditLog auditLog,
        TracebagOptions options,
        ILogger<DiagnosticRunnerService> logger)
    {
        _dockerClientFactory = dockerClientFactory;
        _containerCatalog = containerCatalog;
        _containerPolicy = containerPolicy;
        _dockerLogService = dockerLogService;
        _counterPresetCatalog = counterPresetCatalog;
        _runnerCatalog = runnerCatalog;
        _runnerImages = runnerImages;
        _runnerPolicy = runnerPolicy;
        _counterSampleParser = counterSampleParser;
        _sessionRegistry = sessionRegistry;
        _auditLog = auditLog;
        _options = options;
        _logger = logger;
    }

    public async Task<IReadOnlyList<DotnetProcessDto>> ListDotnetProcessesAsync(string containerId, string user, CancellationToken cancellationToken)
    {
        var container = await _containerCatalog.GetAllowedDotnetAsync(containerId, cancellationToken);
        var identity = _containerPolicy.GetIdentity(container);
        var runner = _runnerCatalog.Select(container);
        await _runnerImages.EnsureAvailableAsync(runner, cancellationToken);
        var result = await RunOneShotAsync(
            container,
            runner,
            DiagnosticRunnerOperation.ProcessDiscovery,
            ["processes"],
            timeout: TimeSpan.FromSeconds(20),
            cancellationToken);

        if (result.ExitCode != 0)
        {
            throw new TracebagException(
                StatusCodes.Status500InternalServerError,
                "dotnet_processes_failed",
                "dotnet-counters ps failed. Ensure the target container mounts the configured shared /tmp volume and DOTNET_EnableDiagnostics=1 is set.");
        }

        await _auditLog.WriteAsync(user, "dotnet.processes.list", identity.Id, _containerPolicy.GetContainerName(container), "success", new
        {
            dockerId = container.ID
        }, cancellationToken);
        return ParseProcesses(result.Output);
    }

    public async Task<CounterSessionResponse> StartCounterSessionAsync(
        string containerId,
        CounterSessionRequest request,
        string user,
        CancellationToken cancellationToken)
    {
        if (request.ProcessId <= 0)
        {
            throw new TracebagException(StatusCodes.Status400BadRequest, "process_id_invalid", "A valid process id is required.");
        }

        var providers = _counterPresetCatalog.GetProviders(request.Preset);
        var container = await _containerCatalog.GetAllowedDotnetAsync(containerId, cancellationToken);
        var identity = _containerPolicy.GetIdentity(container);
        var runner = _runnerCatalog.Select(container);
        await _runnerImages.EnsureAvailableAsync(runner, cancellationToken);
        using var reservation = _sessionRegistry.ReserveTarget(identity.Id);
        var sessionId = CreateId("ctr");
        var runnerName = $"tracebag-runner-{_options.Stage}-{sessionId}";
        var command = BuildCounterCollectLoopCommand(request.ProcessId, providers, sessionId);
        var parameters = _runnerPolicy.Build(new DiagnosticRunnerContainerRequest(
            container,
            runner,
            DiagnosticRunnerOperation.LiveCounters,
            sessionId,
            runnerName,
            command));
        var created = await _dockerClientFactory.Client.Containers.CreateContainerAsync(parameters, cancellationToken);

        try
        {
            await _dockerClientFactory.Client.Containers.StartContainerAsync(created.ID, new ContainerStartParameters(), cancellationToken);
            var session = new DiagnosticSession(
                sessionId,
                identity.Id,
                _containerPolicy.GetContainerName(container),
                created.ID,
                runnerName,
                DateTimeOffset.UtcNow,
                user);
            _sessionRegistry.Add(session);
            _ = StopSessionAfterTimeoutAsync(sessionId);

            await _auditLog.WriteAsync(user, "dotnet.counters.start", identity.Id, session.TargetContainerName, "success", new
            {
                sessionId,
                dockerId = container.ID,
                request.ProcessId,
                request.Preset
            }, cancellationToken);

            return new CounterSessionResponse(sessionId, "running");
        }
        catch
        {
            await RemoveRunnerContainerAsync(created.ID, CancellationToken.None);
            throw;
        }
    }

    public async IAsyncEnumerable<DiagnosticCounterStreamItem> StreamSessionAsync(
        string sessionId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var session = _sessionRegistry.Get(sessionId);
        var completed = false;
        try
        {
            await foreach (var logEvent in _dockerLogService.StreamRunnerLogsAsync(session.RunnerContainerId, cancellationToken))
            {
                if (logEvent.Stream == "stdout"
                    && _counterSampleParser.TryParseMetric(logEvent.Line, DateTimeOffset.UtcNow, out var metric))
                {
                    yield return new DiagnosticCounterStreamItem(metric, null);
                }
                else
                {
                    yield return new DiagnosticCounterStreamItem(null, logEvent);
                }
            }

            completed = true;
        }
        finally
        {
            if (completed && _sessionRegistry.Remove(sessionId, out var completedSession) && completedSession is not null)
            {
                await RemoveRunnerContainerAsync(completedSession.RunnerContainerId, CancellationToken.None);
                await _auditLog.WriteAsync(
                    completedSession.User,
                    "dotnet.counters.complete",
                    completedSession.TargetContainerId,
                    completedSession.TargetContainerName,
                    "success",
                    new { sessionId },
                    CancellationToken.None);
            }
        }
    }

    public async Task StopSessionAsync(string sessionId, string user, CancellationToken cancellationToken)
    {
        if (!_sessionRegistry.Remove(sessionId, out var session) || session is null)
        {
            throw new TracebagException(StatusCodes.Status404NotFound, "session_not_found", "The requested diagnostic session was not found.");
        }

        await StopAndRemoveRunnerAsync(session.RunnerContainerId, cancellationToken);
        await _auditLog.WriteAsync(user, "dotnet.counters.stop", session.TargetContainerId, session.TargetContainerName, "success", new
        {
            sessionId
        }, cancellationToken);
    }

    private async Task<OneShotResult> RunOneShotAsync(
        ContainerListResponse targetContainer,
        DiagnosticRunnerSelection runner,
        DiagnosticRunnerOperation operation,
        IReadOnlyList<string> command,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var sessionId = CreateId("run");
        var runnerName = $"tracebag-runner-{_options.Stage}-{sessionId}";
        var parameters = _runnerPolicy.Build(new DiagnosticRunnerContainerRequest(
            targetContainer,
            runner,
            operation,
            sessionId,
            runnerName,
            command));
        var created = await _dockerClientFactory.Client.Containers.CreateContainerAsync(parameters, cancellationToken);
        try
        {
            await _dockerClientFactory.Client.Containers.StartContainerAsync(created.ID, new ContainerStartParameters(), cancellationToken);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);
            var wait = await _dockerClientFactory.Client.Containers.WaitContainerAsync(created.ID, timeoutCts.Token);
            var output = await _dockerLogService.CollectRunnerLogsAsync(created.ID, TimeSpan.FromSeconds(10), cancellationToken);
            return new OneShotResult(wait.StatusCode, output);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TracebagException(StatusCodes.Status504GatewayTimeout, "runner_timeout", "The diagnostic runner timed out.");
        }
        finally
        {
            await RemoveRunnerContainerAsync(created.ID, CancellationToken.None);
        }
    }

    private async Task StopAndRemoveRunnerAsync(string runnerContainerId, CancellationToken cancellationToken)
    {
        try
        {
            await _dockerClientFactory.Client.Containers.StopContainerAsync(
                runnerContainerId,
                new ContainerStopParameters { WaitBeforeKillSeconds = 5 },
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Runner stop failed or runner was already stopped.");
        }

        await RemoveRunnerContainerAsync(runnerContainerId, cancellationToken);
    }

    private async Task RemoveRunnerContainerAsync(string runnerContainerId, CancellationToken cancellationToken)
    {
        try
        {
            await _dockerClientFactory.Client.Containers.RemoveContainerAsync(
                runnerContainerId,
                new ContainerRemoveParameters { Force = true, RemoveVolumes = false },
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Runner cleanup failed or runner was already removed.");
        }
    }

    private async Task StopSessionAfterTimeoutAsync(string sessionId)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(_options.CounterMaxSeconds));
            if (_sessionRegistry.Remove(sessionId, out var session) && session is not null)
            {
                await StopAndRemoveRunnerAsync(session.RunnerContainerId, CancellationToken.None);
                await _auditLog.WriteAsync(session.User, "dotnet.counters.timeout", session.TargetContainerId, session.TargetContainerName, "success", new
                {
                    sessionId
                }, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Counter session timeout cleanup failed for {SessionId}.", sessionId);
        }
    }

    private static List<DotnetProcessDto> ParseProcesses(string output)
    {
        var processes = new List<DotnetProcessDto>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var match = ProcessLinePattern.Match(line);
            if (!match.Success || !int.TryParse(match.Groups["pid"].Value, out var pid))
            {
                continue;
            }

            var name = match.Groups["name"].Value;
            var commandLine = match.Groups["cmd"].Value.Trim();
            if (IsTracebagToolProcess(name, commandLine))
            {
                continue;
            }

            processes.Add(new DotnetProcessDto(pid, name, string.IsNullOrWhiteSpace(commandLine) ? name : commandLine));
        }

        return processes;
    }

    private static bool IsTracebagToolProcess(string name, string commandLine)
    {
        return IsDiagnosticToolName(name)
            || commandLine.Contains("dotnet-counters", StringComparison.OrdinalIgnoreCase)
            || commandLine.Contains("dotnet-trace", StringComparison.OrdinalIgnoreCase)
            || commandLine.Contains("dotnet-dump", StringComparison.OrdinalIgnoreCase)
            || commandLine.Contains("dotnet-gcdump", StringComparison.OrdinalIgnoreCase)
            || commandLine.Contains("dotnet-stack", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDiagnosticToolName(string name)
    {
        return name.Equals("dotnet-counters", StringComparison.OrdinalIgnoreCase)
            || name.Equals("dotnet-trace", StringComparison.OrdinalIgnoreCase)
            || name.Equals("dotnet-dump", StringComparison.OrdinalIgnoreCase)
            || name.Equals("dotnet-gcdump", StringComparison.OrdinalIgnoreCase)
            || name.Equals("dotnet-stack", StringComparison.OrdinalIgnoreCase)
            || name.Equals(".NET EventPipe", StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateId(string prefix)
    {
        return $"{prefix}-{Guid.NewGuid():N}";
    }

    private static IReadOnlyList<string> BuildCounterCollectLoopCommand(int processId, IReadOnlyList<string> providers, string sessionId)
    {
        return ["counter-loop", processId.ToString(CultureInfo.InvariantCulture), string.Join(',', providers), sessionId];
    }

    private sealed record OneShotResult(long ExitCode, string Output);
}

public sealed record DiagnosticCounterStreamItem(CounterMetric? Metric, LogEventDto? Output);
