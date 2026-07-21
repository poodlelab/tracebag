using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Docker.DotNet.Models;
using Tracebag.Api.Artifacts;
using Tracebag.Api.Audit;
using Tracebag.Api.Auth;
using Tracebag.Api.Docker;
using Tracebag.Api.Models;
using Tracebag.Api.Security;

namespace Tracebag.Api.Diagnostics;

public sealed class DiagnosticJobService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<string, ActiveJob> _active = new(StringComparer.Ordinal);
    private readonly DiagnosticJobStore _store;
    private readonly DiagnosticJobProfileCatalog _profiles;
    private readonly DockerClientFactory _docker;
    private readonly ContainerCatalog _containers;
    private readonly ContainerPolicy _containerPolicy;
    private readonly DiagnosticRunnerCatalog _runners;
    private readonly DiagnosticRunnerImageService _runnerImages;
    private readonly DiagnosticRunnerContainerPolicy _runnerPolicy;
    private readonly DockerLogService _dockerLogs;
    private readonly ArtifactStore _artifacts;
    private readonly AuditLog _audit;
    private readonly TracebagOptions _options;
    private readonly ILogger<DiagnosticJobService> _logger;

    public DiagnosticJobService(
        DiagnosticJobStore store,
        DiagnosticJobProfileCatalog profiles,
        DockerClientFactory docker,
        ContainerCatalog containers,
        ContainerPolicy containerPolicy,
        DiagnosticRunnerCatalog runners,
        DiagnosticRunnerImageService runnerImages,
        DiagnosticRunnerContainerPolicy runnerPolicy,
        DockerLogService dockerLogs,
        ArtifactStore artifacts,
        AuditLog audit,
        TracebagOptions options,
        ILogger<DiagnosticJobService> logger)
    {
        _store = store;
        _profiles = profiles;
        _docker = docker;
        _containers = containers;
        _containerPolicy = containerPolicy;
        _runners = runners;
        _runnerImages = runnerImages;
        _runnerPolicy = runnerPolicy;
        _dockerLogs = dockerLogs;
        _artifacts = artifacts;
        _audit = audit;
        _options = options;
        _logger = logger;
    }

    public IReadOnlyList<DiagnosticJobProfileResponse> ListProfiles() => _profiles.List();

    public Task<IReadOnlyList<DiagnosticJobResponse>> ListAsync(string? containerId, string? status, CancellationToken cancellationToken) =>
        _store.ListAsync(containerId, status, cancellationToken);

    public Task<DiagnosticJobResponse> GetAsync(string jobId, CancellationToken cancellationToken) => _store.GetAsync(jobId, cancellationToken);

    public Task<IReadOnlyList<DiagnosticJobEventResponse>> GetEventsAsync(string jobId, long afterId, CancellationToken cancellationToken) =>
        _store.GetEventsAsync(jobId, afterId, cancellationToken);

    public async Task<DiagnosticJobResponse> CreateAsync(
        string containerId,
        DiagnosticJobCreateRequest request,
        string? idempotencyKey,
        string user,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Profile))
        {
            throw new TracebagException(StatusCodes.Status400BadRequest, "diagnostic_profile_required", "A diagnostic profile is required.");
        }
        var normalizedKey = NormalizeIdempotencyKey(idempotencyKey, user);
        var target = await _containers.GetAllowedDotnetAsync(containerId, cancellationToken);
        var identity = _containerPolicy.GetIdentity(target);
        var runner = _runners.Select(target);
        var jobId = $"job-{Guid.NewGuid():N}";
        var stagingFileName = $"{jobId}.capture";
        ResolvedDiagnosticProfile resolved;
        try
        {
            resolved = _profiles.Resolve(request, target, stagingFileName);
        }
        catch (TracebagException ex)
        {
            await _audit.WriteAsync(user, "diagnostic.job.reject", identity.Id, _containerPolicy.GetContainerName(target), "failed", new
            {
                profile = Truncate(request.Profile, 60),
                request.ProcessId,
                errorCode = ex.Code
            }, cancellationToken);
            throw;
        }
        // Keep the real extension last. Some fixed diagnostic tools (notably
        // dotnet-gcdump) append their extension when the output path does not
        // already end with it, which would make the registered staging path
        // differ from the file the runner actually created.
        stagingFileName = BuildStagingFileName(jobId, resolved.Extension);
        resolved = _profiles.Resolve(request, target, stagingFileName);
        var createdAt = DateTimeOffset.UtcNow;
        var inputs = new
        {
            processId = request.ProcessId,
            profile = resolved.Id,
            durationSeconds = resolved.DurationSeconds
        };
        var inputsJson = JsonSerializer.Serialize(inputs, JsonOptions);
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{identity.Id}\n{inputsJson}"))).ToLowerInvariant();
        DiagnosticJobResponse reserved;
        try
        {
            reserved = await _store.ReserveAsync(new DiagnosticJobReservation(
                jobId,
                identity.Id,
                _containerPolicy.GetContainerName(target),
                target.ID,
                request.ProcessId,
                resolved.Id,
                createdAt,
                createdAt.AddSeconds(resolved.TimeoutSeconds),
                user,
                normalizedKey,
                fingerprint,
                inputsJson,
                runner), cancellationToken);
        }
        catch (TracebagException ex)
        {
            await _audit.WriteAsync(user, "diagnostic.job.reject", identity.Id, _containerPolicy.GetContainerName(target), "failed", new
            {
                profile = resolved.Id,
                request.ProcessId,
                errorCode = ex.Code
            }, cancellationToken);
            throw;
        }

        if (!string.Equals(reserved.Id, jobId, StringComparison.Ordinal))
        {
            return reserved;
        }

        var active = new ActiveJob(jobId, target, runner, resolved, stagingFileName, inputsJson, user);
        if (!_active.TryAdd(jobId, active))
        {
            throw new InvalidOperationException("The diagnostic job was reserved twice.");
        }

        _ = Task.Run(() => ExecuteAsync(active), CancellationToken.None);

        await _audit.WriteAsync(user, "diagnostic.job.create", identity.Id, reserved.ContainerName, "success", new
        {
            jobId,
            profile = resolved.Id,
            request.ProcessId,
            durationSeconds = resolved.DurationSeconds,
            runtimeMajor = runner.RuntimeMajor,
            runnerImage = runner.Image,
            toolVersion = runner.ToolVersion
        }, cancellationToken);
        return reserved;
    }

    public async Task<DiagnosticJobResponse> CancelAsync(string jobId, string user, CancellationToken cancellationToken)
    {
        var job = await _store.RequestCancellationAsync(jobId, cancellationToken);
        if (DiagnosticJobStore.IsTerminal(job.Status))
        {
            return job;
        }

        if (_active.TryGetValue(jobId, out var active))
        {
            active.Cancellation.Cancel();
            var runnerId = active.RunnerContainerId;
            if (!string.IsNullOrWhiteSpace(runnerId))
            {
                await StopAndRemoveRunnerAsync(runnerId, cancellationToken);
            }
        }

        await _audit.WriteAsync(user, "diagnostic.job.cancel", job.ContainerId, job.ContainerName, "success", new { jobId }, cancellationToken);
        return await _store.GetAsync(jobId, cancellationToken);
    }

    public async Task RecoverAfterRestartAsync(CancellationToken cancellationToken)
    {
        var interrupted = await _store.MarkActiveInterruptedAsync(cancellationToken);
        var removed = await RemoveOrphanJobRunnersAsync(cancellationToken);
        var artifacts = await _artifacts.ReconcileAsync(cancellationToken);
        foreach (var job in interrupted)
        {
            await _audit.WriteAsync("system", "diagnostic.job.interrupted", job.ContainerId, job.ContainerName, "failed", new { jobId = job.Id }, cancellationToken);
        }
        if (removed > 0 || artifacts.MissingFiles > 0 || artifacts.QuarantinedFiles > 0)
        {
            await _audit.WriteAsync("system", "diagnostic.reconcile", null, null, "success", new
            {
                removedRunners = removed,
                missingArtifacts = artifacts.MissingFiles,
                quarantinedFiles = artifacts.QuarantinedFiles
            }, cancellationToken);
        }
    }

    private async Task ExecuteAsync(ActiveJob active)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(active.Profile.TimeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(active.Cancellation.Token, timeout.Token);
        var token = linked.Token;
        try
        {
            await _store.TransitionAsync(active.Id, "validating", 10, "Target and fixed capture profile validated.", cancellationToken: token);
            await _runnerImages.EnsureAvailableAsync(active.Runner, token);
            await _store.TransitionAsync(active.Id, "starting", 20, "Creating the isolated diagnostic runner.", cancellationToken: token);
            var runnerName = $"tracebag-job-runner-{_options.Stage}-{active.Id}";
            var parameters = _runnerPolicy.Build(new DiagnosticRunnerContainerRequest(
                active.Target,
                active.Runner,
                active.Profile.Operation,
                active.Id,
                runnerName,
                active.Profile.Command,
                active.Profile.Id));
            var created = await _docker.Client.Containers.CreateContainerAsync(parameters, token);
            active.RunnerContainerId = created.ID;
            await _docker.Client.Containers.StartContainerAsync(created.ID, new ContainerStartParameters(), token);
            await _store.TransitionAsync(active.Id, "running", 35, "Diagnostic capture is running.", runnerContainerId: created.ID, cancellationToken: token);

            var wait = await _docker.Client.Containers.WaitContainerAsync(created.ID, token);
            var output = await _dockerLogs.CollectRunnerLogsAsync(created.ID, TimeSpan.FromSeconds(10), CancellationToken.None);
            if (wait.StatusCode != 0)
            {
                var targetExited = await TargetExitedAsync(active.Target.ID);
                await _store.TransitionAsync(
                    active.Id,
                    targetExited ? "target_exited" : "failed",
                    90,
                    targetExited ? "The target exited during capture." : "The diagnostic tool exited unsuccessfully.",
                    errorCode: targetExited ? "target_exited" : "runner_failed",
                    errorMessage: Truncate(output, 1200),
                    cancellationToken: CancellationToken.None);
                await AuditCompletionAsync(active, targetExited ? "target_exited" : "failed", output);
                return;
            }

            await _store.TransitionAsync(active.Id, "collecting", 85, "Hashing output and writing the artifact manifest.", cancellationToken: token);
            var artifactId = $"artifact-{Guid.NewGuid():N}";
            var inputs = JsonSerializer.Deserialize<object>(active.InputsJson, JsonOptions) ?? new { };
            var outcome = new { runnerExitCode = wait.StatusCode, output = Truncate(output, 600) };
            var artifact = await _artifacts.RegisterJobArtifactAsync(
                artifactId,
                active.Id,
                _containerPolicy.GetIdentity(active.Target).Id,
                _containerPolicy.GetContainerName(active.Target),
                active.Profile.Id,
                active.StagingFileName,
                active.Profile.Extension,
                active.User,
                active.ProcessId,
                active.Runner.RuntimeMajor,
                active.Runner.Image,
                active.Runner.ToolVersion,
                inputs,
                outcome,
                token);
            await _store.TransitionAsync(
                active.Id,
                "completed",
                100,
                "Capture completed and the artifact is ready.",
                artifactId: artifact.Id,
                outcomeJson: JsonSerializer.Serialize(outcome, JsonOptions),
                cancellationToken: CancellationToken.None);
            await AuditCompletionAsync(active, "completed", null);
        }
        catch (OperationCanceledException)
        {
            var timedOut = timeout.IsCancellationRequested && !active.Cancellation.IsCancellationRequested;
            await _store.TransitionAsync(
                active.Id,
                timedOut ? "timed_out" : "cancelled",
                timedOut ? 99 : 0,
                timedOut ? "The diagnostic capture exceeded its server-owned timeout." : "The diagnostic capture was cancelled.",
                errorCode: timedOut ? "runner_timeout" : null,
                cancellationToken: CancellationToken.None);
            await AuditCompletionAsync(active, timedOut ? "timed_out" : "cancelled", null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Diagnostic job {JobId} failed.", active.Id);
            await _store.TransitionAsync(active.Id, "failed", 99, "The diagnostic capture failed.", errorCode: "diagnostic_failed", errorMessage: Truncate(ex.Message, 1200), cancellationToken: CancellationToken.None);
            await AuditCompletionAsync(active, "failed", ex.Message);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(active.RunnerContainerId))
            {
                await StopAndRemoveRunnerAsync(active.RunnerContainerId, CancellationToken.None);
            }
            else
            {
                await RemoveRunnersForJobAsync(active.Id, CancellationToken.None);
            }

            var stagingPath = _artifacts.GetArtifactPath(active.StagingFileName);
            if (File.Exists(stagingPath))
            {
                File.Delete(stagingPath);
            }

            _active.TryRemove(active.Id, out _);
            active.Cancellation.Dispose();
        }
    }

    private async Task<bool> TargetExitedAsync(string targetId)
    {
        try
        {
            var target = await _docker.Client.Containers.InspectContainerAsync(targetId, CancellationToken.None);
            return target.State?.Running != true;
        }
        catch
        {
            return true;
        }
    }

    private async Task<int> RemoveOrphanJobRunnersAsync(CancellationToken cancellationToken)
    {
        var removed = 0;
        var containers = await _docker.Client.Containers.ListContainersAsync(new ContainersListParameters { All = true }, cancellationToken);
        foreach (var container in containers)
        {
            var labels = container.Labels ?? new Dictionary<string, string>();
            if (labels.TryGetValue("tracebag.diagnosticJob", out var jobLabel)
                && string.Equals(jobLabel, "true", StringComparison.OrdinalIgnoreCase)
                && labels.TryGetValue("tracebag.instance", out var instance)
                && string.Equals(instance, _options.Stage, StringComparison.OrdinalIgnoreCase))
            {
                await StopAndRemoveRunnerAsync(container.ID, cancellationToken);
                removed++;
            }
        }
        return removed;
    }

    private async Task StopAndRemoveRunnerAsync(string runnerId, CancellationToken cancellationToken)
    {
        try { await _docker.Client.Containers.StopContainerAsync(runnerId, new ContainerStopParameters { WaitBeforeKillSeconds = 5 }, cancellationToken); }
        catch (Exception ex) { _logger.LogDebug(ex, "Diagnostic runner was already stopped."); }
        try { await _docker.Client.Containers.RemoveContainerAsync(runnerId, new ContainerRemoveParameters { Force = true, RemoveVolumes = false }, cancellationToken); }
        catch (Exception ex) { _logger.LogDebug(ex, "Diagnostic runner was already removed."); }
    }

    private async Task RemoveRunnersForJobAsync(string jobId, CancellationToken cancellationToken)
    {
        try
        {
            var containers = await _docker.Client.Containers.ListContainersAsync(new ContainersListParameters { All = true }, cancellationToken);
            foreach (var container in containers)
            {
                var labels = container.Labels ?? new Dictionary<string, string>();
                if (labels.TryGetValue("tracebag.diagnosticJobId", out var value)
                    && string.Equals(value, jobId, StringComparison.Ordinal))
                {
                    await StopAndRemoveRunnerAsync(container.ID, cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not complete fallback runner cleanup for job {JobId}.", jobId);
        }
    }

    private Task AuditCompletionAsync(ActiveJob active, string status, string? error) => _audit.WriteAsync(
        active.User,
        $"diagnostic.job.{status}",
        _containerPolicy.GetIdentity(active.Target).Id,
        _containerPolicy.GetContainerName(active.Target),
        status == "completed" || status == "cancelled" ? "success" : "failed",
        new { jobId = active.Id, profile = active.Profile.Id, error = Truncate(error, 600) },
        CancellationToken.None);

    private static string? NormalizeIdempotencyKey(string? value, string user)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > 120 || trimmed.Any(ch => !(char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' or ':')))
        {
            throw new TracebagException(StatusCodes.Status400BadRequest, "idempotency_key_invalid", "Idempotency-Key must be at most 120 characters using letters, numbers, dot, colon, dash or underscore.");
        }
        return $"{user}:{trimmed}";
    }

    private static string? Truncate(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null : value[..Math.Min(value.Length, max)];

    internal static string BuildStagingFileName(string jobId, string extension) => $"{jobId}.capture.{extension}";

    public void Dispose()
    {
        foreach (var job in _active.Values)
        {
            job.Cancellation.Cancel();
        }
    }

    private sealed class ActiveJob(
        string id,
        ContainerListResponse target,
        DiagnosticRunnerSelection runner,
        ResolvedDiagnosticProfile profile,
        string stagingFileName,
        string inputsJson,
        string user)
    {
        public string Id { get; } = id;
        public ContainerListResponse Target { get; } = target;
        public DiagnosticRunnerSelection Runner { get; } = runner;
        public ResolvedDiagnosticProfile Profile { get; } = profile;
        public string StagingFileName { get; } = stagingFileName;
        public string InputsJson { get; } = inputsJson;
        public string User { get; } = user;
        public int ProcessId { get; } = JsonSerializer.Deserialize<JsonElement>(inputsJson).GetProperty("processId").GetInt32();
        public CancellationTokenSource Cancellation { get; } = new();
        public string? RunnerContainerId { get; set; }
    }
}
