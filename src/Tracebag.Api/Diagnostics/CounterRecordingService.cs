using System.Collections.Concurrent;
using System.Globalization;
using Docker.DotNet.Models;
using Tracebag.Api.Audit;
using Tracebag.Api.Auth;
using Tracebag.Api.Docker;
using Tracebag.Api.Models;
using Tracebag.Api.Security;

namespace Tracebag.Api.Diagnostics;

public sealed class CounterRecordingService : IDisposable
{
    private static readonly int[] AllowedIntervals = [2, 5, 10];
    private readonly ConcurrentDictionary<string, ActiveRecording> _activeRecordings = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _startLock = new(1, 1);
    private readonly DockerClientFactory _dockerClientFactory;
    private readonly ContainerCatalog _containerCatalog;
    private readonly ContainerPolicy _containerPolicy;
    private readonly DockerLogService _dockerLogService;
    private readonly CounterPresetCatalog _counterPresetCatalog;
    private readonly DiagnosticRunnerCatalog _runnerCatalog;
    private readonly DiagnosticRunnerImageService _runnerImages;
    private readonly DiagnosticRunnerContainerPolicy _runnerPolicy;
    private readonly CounterRecordingStore _store;
    private readonly CounterSampleParser _parser;
    private readonly AuditLog _auditLog;
    private readonly TracebagOptions _options;
    private readonly ILogger<CounterRecordingService> _logger;

    public CounterRecordingService(
        DockerClientFactory dockerClientFactory,
        ContainerCatalog containerCatalog,
        ContainerPolicy containerPolicy,
        DockerLogService dockerLogService,
        CounterPresetCatalog counterPresetCatalog,
        DiagnosticRunnerCatalog runnerCatalog,
        DiagnosticRunnerImageService runnerImages,
        DiagnosticRunnerContainerPolicy runnerPolicy,
        CounterRecordingStore store,
        CounterSampleParser parser,
        AuditLog auditLog,
        TracebagOptions options,
        ILogger<CounterRecordingService> logger)
    {
        _dockerClientFactory = dockerClientFactory;
        _containerCatalog = containerCatalog;
        _containerPolicy = containerPolicy;
        _dockerLogService = dockerLogService;
        _counterPresetCatalog = counterPresetCatalog;
        _runnerCatalog = runnerCatalog;
        _runnerImages = runnerImages;
        _runnerPolicy = runnerPolicy;
        _store = store;
        _parser = parser;
        _auditLog = auditLog;
        _options = options;
        _logger = logger;
    }

    public async Task<IReadOnlyList<CounterRecordingResponse>> ListAsync(
        string? status,
        string? containerId,
        CancellationToken cancellationToken)
    {
        return await _store.ListAsync(status, containerId, cancellationToken);
    }

    public async Task<CounterRecordingDetailResponse> GetDetailAsync(string recordingId, CancellationToken cancellationToken)
    {
        return await _store.GetDetailAsync(recordingId, cancellationToken);
    }

    public async Task<CounterRecordingSamplesResponse> GetSamplesAsync(
        string recordingId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? resolution,
        CancellationToken cancellationToken)
    {
        return await _store.GetSamplesAsync(recordingId, from, to, resolution, cancellationToken);
    }

    public async Task<CounterRecordingStartResponse> StartAsync(
        string containerId,
        CounterRecordingStartRequest request,
        string user,
        CancellationToken cancellationToken)
    {
        EnsureRecordingEnabled();
        if (request.ProcessId <= 0)
        {
            throw new TracebagException(StatusCodes.Status400BadRequest, "process_id_invalid", "A valid process id is required.");
        }

        var intervalSeconds = request.IntervalSeconds ?? _options.CounterRecordingDefaultIntervalSeconds;
        if (!AllowedIntervals.Contains(intervalSeconds))
        {
            throw new TracebagException(StatusCodes.Status400BadRequest, "counter_recording_interval_invalid", "Allowed counter recording intervals are 2, 5 and 10 seconds.");
        }

        var maxDurationMinutes = Math.Clamp(
            request.MaxDurationMinutes ?? 60,
            1,
            _options.CounterRecordingMaxDurationMinutes);
        if (string.IsNullOrWhiteSpace(request.Preset))
        {
            throw new TracebagException(StatusCodes.Status400BadRequest, "counter_preset_required", "A counter preset is required.");
        }

        var providers = _counterPresetCatalog.GetProviders(request.Preset);
        var targetContainer = await _containerCatalog.GetAllowedDotnetAsync(containerId, cancellationToken);
        var targetIdentity = _containerPolicy.GetIdentity(targetContainer);
        var targetContainerName = _containerPolicy.GetContainerName(targetContainer);
        var runner = _runnerCatalog.Select(targetContainer);
        await _runnerImages.EnsureAvailableAsync(runner, cancellationToken);

        await _startLock.WaitAsync(cancellationToken);
        try
        {
            var recordingId = CreateId("rec");
            var runnerName = $"tracebag-recording-runner-{_options.Stage}-{recordingId}";
            await _store.ReserveAsync(
                recordingId,
                targetIdentity.Id,
                targetContainerName,
                request.ProcessId,
                request.Preset,
                providers,
                intervalSeconds,
                maxDurationMinutes * 60,
                request.Name,
                user,
                runner,
                cancellationToken);

            CreateContainerResponse? created = null;
            try
            {
                var command = BuildCounterRecordingCommand(request.ProcessId, providers, recordingId, intervalSeconds);
                var parameters = _runnerPolicy.Build(new DiagnosticRunnerContainerRequest(
                    targetContainer,
                    runner,
                    DiagnosticRunnerOperation.CounterRecording,
                    recordingId,
                    runnerName,
                    command));
                created = await _dockerClientFactory.Client.Containers.CreateContainerAsync(parameters, cancellationToken);
                await _dockerClientFactory.Client.Containers.StartContainerAsync(created.ID, new ContainerStartParameters(), cancellationToken);
                await _store.MarkRunningAsync(recordingId, created.ID, cancellationToken);

                var active = new ActiveRecording(recordingId, targetIdentity.Id, targetContainerName, created.ID, new CancellationTokenSource())
                {
                    User = user
                };
                if (!_activeRecordings.TryAdd(recordingId, active))
                {
                    throw new TracebagException(StatusCodes.Status409Conflict, "counter_recording_already_running", "The counter recording is already active.");
                }

                _ = Task.Run(() => IngestAsync(active), CancellationToken.None);
                _ = Task.Run(() => StopAfterTimeoutAsync(recordingId, TimeSpan.FromMinutes(maxDurationMinutes)), CancellationToken.None);

                await _auditLog.WriteAsync(user, "dotnet.recording.start", targetIdentity.Id, targetContainerName, "success", new
                {
                    recordingId,
                    dockerId = targetContainer.ID,
                    request.ProcessId,
                    request.Preset,
                    intervalSeconds,
                    maxDurationMinutes,
                    runtimeMajor = runner.RuntimeMajor,
                    runnerImage = runner.Image,
                    toolVersion = runner.ToolVersion
                }, cancellationToken);

                return new CounterRecordingStartResponse(recordingId, "running");
            }
            catch (Exception ex)
            {
                if (created is not null)
                {
                    await RemoveRunnerContainerAsync(created.ID, CancellationToken.None);
                }

                await _store.MarkFinishedAsync(recordingId, "failed", "start_failed", ex.Message, CancellationToken.None);
                await _auditLog.WriteAsync(user, "dotnet.recording.start", targetIdentity.Id, targetContainerName, "failed", new
                {
                    recordingId,
                    dockerId = targetContainer.ID,
                    error = ex.Message
                }, CancellationToken.None);
                throw;
            }
        }
        finally
        {
            _startLock.Release();
        }
    }

    public async Task<CounterRecordingResponse> StopAsync(string recordingId, string user, CancellationToken cancellationToken)
    {
        var recording = await StopInternalAsync(recordingId, user, "stopped", "manual", null, cancellationToken);
        return recording;
    }

    public async Task<CounterRecordingResponse> UpdateAsync(
        string recordingId,
        CounterRecordingUpdateRequest request,
        string user,
        CancellationToken cancellationToken)
    {
        var updated = await _store.UpdateMetadataAsync(recordingId, request.Name, request.Notes, cancellationToken);
        await _auditLog.WriteAsync(user, "dotnet.recording.update", updated.ContainerId, updated.ContainerName, "success", new
        {
            recordingId,
            hasNotes = !string.IsNullOrWhiteSpace(updated.Notes)
        }, cancellationToken);
        return updated;
    }

    public async Task<CounterRecordingExport> ExportAsync(string recordingId, string? format, CancellationToken cancellationToken)
    {
        return await _store.ExportAsync(recordingId, format, cancellationToken);
    }

    public async Task DeleteAsync(string recordingId, string? confirmation, string user, CancellationToken cancellationToken)
    {
        var recording = await _store.GetAsync(recordingId, cancellationToken);
        if (IsActive(recording.Status))
        {
            throw new TracebagException(
                StatusCodes.Status409Conflict,
                "recording_delete_active",
                "Stop the active recording before deleting it.");
        }

        if (!string.Equals(confirmation, recordingId, StringComparison.Ordinal))
        {
            throw new TracebagException(
                StatusCodes.Status400BadRequest,
                "recording_delete_confirmation_required",
                "Deletion requires the exact recording id in the confirm query parameter.");
        }

        if (await _store.IsReferencedByIncidentAsync(recordingId, cancellationToken))
        {
            throw new TracebagException(
                StatusCodes.Status409Conflict,
                "recording_referenced_by_incident",
                "This recording is evidence in an incident and cannot be deleted independently.");
        }

        await _store.DeleteAsync(recordingId, cancellationToken);
        await _auditLog.WriteAsync(user, "dotnet.recording.delete", recording.ContainerId, recording.ContainerName, "success", new
        {
            recordingId
        }, cancellationToken);
    }

    public async Task RecoverAfterRestartAsync(CancellationToken cancellationToken)
    {
        var interrupted = await _store.MarkActiveInterruptedAsync(cancellationToken);
        await RemoveLeftoverRecordingRunnersAsync(cancellationToken);
        foreach (var recording in interrupted)
        {
            await _auditLog.WriteAsync("system", "dotnet.recording.interrupted", recording.ContainerId, recording.ContainerName, "failed", new
            {
                recordingId = recording.Id
            }, cancellationToken);
        }
    }

    public async Task ApplyRetentionAsync(CancellationToken cancellationToken)
    {
        await _store.ApplyRetentionAsync(cancellationToken);
    }

    private async Task IngestAsync(ActiveRecording active)
    {
        var batch = new List<CounterSample>();
        var stderr = new List<string>();
        var lastFlushAt = DateTimeOffset.UtcNow;
        try
        {
            await foreach (var logEvent in _dockerLogService.StreamRunnerLogsAsync(active.RunnerContainerId, active.Cancellation.Token))
            {
                if (logEvent.Stream == "stderr")
                {
                    if (!string.IsNullOrWhiteSpace(logEvent.Line))
                    {
                        stderr.Add(logEvent.Line);
                        if (stderr.Count > 20)
                        {
                            stderr.RemoveAt(0);
                        }
                    }

                    continue;
                }

                if (_parser.TryParse(logEvent.Line, out var sample))
                {
                    batch.Add(sample);
                }

                if (batch.Count >= 100 || (batch.Count > 0 && DateTimeOffset.UtcNow - lastFlushAt >= TimeSpan.FromSeconds(5)))
                {
                    await FlushBatchAsync(active.Id, batch, active.Cancellation.Token);
                    lastFlushAt = DateTimeOffset.UtcNow;
                }
            }

            await FlushBatchAsync(active.Id, batch, CancellationToken.None);
            if (_activeRecordings.TryRemove(active.Id, out _))
            {
                await RemoveRunnerContainerAsync(active.RunnerContainerId, CancellationToken.None);
                var error = stderr.Count == 0 ? null : string.Join('\n', stderr);
                var status = string.IsNullOrWhiteSpace(error) ? "completed" : "failed";
                var stopReason = string.IsNullOrWhiteSpace(error) ? "runner_completed" : "runner_failed";
                await _store.MarkFinishedAsync(active.Id, status, stopReason, error, CancellationToken.None);
                await _auditLog.WriteAsync(active.User, "dotnet.recording.complete", active.ContainerId, active.ContainerName, status == "completed" ? "success" : "failed", new
                {
                    recordingId = active.Id,
                    error
                }, CancellationToken.None);
            }
        }
        catch (OperationCanceledException)
        {
            await FlushBatchAsync(active.Id, batch, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Counter recording ingest failed for {RecordingId}.", active.Id);
            if (_activeRecordings.TryRemove(active.Id, out _))
            {
                await RemoveRunnerContainerAsync(active.RunnerContainerId, CancellationToken.None);
                await _store.MarkFinishedAsync(active.Id, "failed", "ingest_failed", ex.Message, CancellationToken.None);
                await _auditLog.WriteAsync(active.User, "dotnet.recording.failed", active.ContainerId, active.ContainerName, "failed", new
                {
                    recordingId = active.Id,
                    error = ex.Message
                }, CancellationToken.None);
            }
        }
        finally
        {
            active.Cancellation.Dispose();
        }
    }

    private async Task<CounterRecordingResponse> StopInternalAsync(
        string recordingId,
        string user,
        string status,
        string stopReason,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        var current = await _store.GetAsync(recordingId, cancellationToken);
        await _store.MarkStoppingAsync(recordingId, cancellationToken);
        if (_activeRecordings.TryRemove(recordingId, out var active))
        {
            active.Cancellation.Cancel();
            await StopAndRemoveRunnerAsync(active.RunnerContainerId, cancellationToken);
            await _store.MarkFinishedAsync(recordingId, status, stopReason, errorMessage, cancellationToken);
            await _auditLog.WriteAsync(user, $"dotnet.recording.{stopReason}", current.ContainerId, current.ContainerName, status == "failed" ? "failed" : "success", new
            {
                recordingId
            }, cancellationToken);
        }
        else if (IsActive(current.Status))
        {
            await _store.MarkFinishedAsync(recordingId, status, stopReason, errorMessage, cancellationToken);
        }

        return await _store.GetAsync(recordingId, cancellationToken);
    }

    private async Task StopAfterTimeoutAsync(string recordingId, TimeSpan timeout)
    {
        try
        {
            await Task.Delay(timeout);
            if (_activeRecordings.ContainsKey(recordingId))
            {
                await StopInternalAsync(recordingId, "system", "timed_out", "timeout", null, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Counter recording timeout cleanup failed for {RecordingId}.", recordingId);
        }
    }

    private async Task FlushBatchAsync(string recordingId, List<CounterSample> batch, CancellationToken cancellationToken)
    {
        if (batch.Count == 0)
        {
            return;
        }

        var copy = batch.ToArray();
        batch.Clear();
        await _store.AddSamplesAsync(recordingId, copy, cancellationToken);
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
            _logger.LogDebug(ex, "Counter recording runner stop failed or runner was already stopped.");
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
            _logger.LogDebug(ex, "Counter recording runner cleanup failed or runner was already removed.");
        }
    }

    private async Task RemoveLeftoverRecordingRunnersAsync(CancellationToken cancellationToken)
    {
        try
        {
            var containers = await _dockerClientFactory.Client.Containers.ListContainersAsync(
                new ContainersListParameters { All = true },
                cancellationToken);
            foreach (var container in containers)
            {
                var labels = container.Labels ?? new Dictionary<string, string>();
                if (labels.TryGetValue("tracebag.recording", out var recordingLabel)
                    && string.Equals(recordingLabel, "true", StringComparison.OrdinalIgnoreCase)
                    && labels.TryGetValue("tracebag.instance", out var instance)
                    && string.Equals(instance, _options.Stage, StringComparison.OrdinalIgnoreCase))
                {
                    await RemoveRunnerContainerAsync(container.ID, cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Counter recording runner recovery cleanup failed.");
        }
    }

    private void EnsureRecordingEnabled()
    {
        if (!_options.CounterRecordingEnabled)
        {
            throw new TracebagException(StatusCodes.Status403Forbidden, "counter_recording_disabled", "Counter recording is disabled.");
        }
    }

    private static IReadOnlyList<string> BuildCounterRecordingCommand(
        int processId,
        IReadOnlyList<string> providers,
        string recordingId,
        int intervalSeconds)
    {
        const string chunkDuration = "00:00:10";
        return
        [
            "counter-recording",
            processId.ToString(CultureInfo.InvariantCulture),
            string.Join(',', providers),
            recordingId,
            intervalSeconds.ToString(CultureInfo.InvariantCulture),
            chunkDuration
        ];
    }

    private static bool IsActive(string status)
    {
        return string.Equals(status, "starting", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "running", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "stopping", StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateId(string prefix)
    {
        return $"{prefix}-{Guid.NewGuid():N}";
    }

    public void Dispose()
    {
        foreach (var active in _activeRecordings.Values)
        {
            active.Cancellation.Cancel();
        }

        _startLock.Dispose();
    }

    private sealed record ActiveRecording(
        string Id,
        string ContainerId,
        string ContainerName,
        string RunnerContainerId,
        CancellationTokenSource Cancellation)
    {
        public string User { get; init; } = "system";
    }
}
