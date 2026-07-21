namespace Tracebag.Demo.Api.Scenarios;

public sealed class DemoScenarioCoordinator(
    ILogger<DemoScenarioCoordinator> logger) : IHostedService
{
    private readonly object _gate = new();
    private readonly Dictionary<string, ScenarioExecution> _executions = new(StringComparer.Ordinal);

    public DemoScenarioSnapshot Start(
        string name,
        TimeSpan maximumDuration,
        Func<CancellationToken, Task> workload)
    {
        var execution = CreateExecution(name, maximumDuration);
        execution.Task = Task.Run(() => RunCoreAsync(execution, workload), CancellationToken.None);
        lock (_gate)
        {
            return Snapshot(execution);
        }
    }

    public async Task<DemoScenarioSnapshot> RunInlineAsync(
        string name,
        TimeSpan maximumDuration,
        Func<CancellationToken, Task> workload)
    {
        var execution = CreateExecution(name, maximumDuration);
        execution.Task = RunCoreAsync(execution, workload);
        await execution.Task;
        lock (_gate)
        {
            return Snapshot(execution);
        }
    }

    public DemoStatusResponse Status()
    {
        lock (_gate)
        {
            var scenarios = _executions.Values
                .OrderByDescending(execution => execution.StartedAt)
                .Select(Snapshot)
                .ToArray();
            return new DemoStatusResponse(
                scenarios.Count(scenario => scenario.State is "queued" or "running"),
                scenarios,
                DemoScenarioLimits.Describe());
        }
    }

    public async Task<DemoStatusResponse> ResetAsync(CancellationToken cancellationToken)
    {
        Task[] activeTasks;
        lock (_gate)
        {
            var active = _executions.Values.Where(IsActive).ToArray();
            foreach (var execution in active)
            {
                execution.Cancellation.Cancel();
            }

            activeTasks = active.Select(execution => execution.Task).ToArray();
        }

        if (activeTasks.Length > 0)
        {
            try
            {
                await Task.WhenAll(activeTasks).WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("Timed out while resetting demo scenarios.");
            }
        }

        return Status();
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await ResetAsync(cancellationToken);
        lock (_gate)
        {
            foreach (var execution in _executions.Values)
            {
                execution.Cancellation.Dispose();
            }
        }
    }

    private ScenarioExecution CreateExecution(string name, TimeSpan maximumDuration)
    {
        lock (_gate)
        {
            if (_executions.TryGetValue(name, out var existing) && IsActive(existing))
            {
                throw new DemoScenarioConflictException($"The {name} scenario is already active.");
            }

            if (_executions.Values.Count(IsActive) >= DemoScenarioLimits.MaxActiveScenarios)
            {
                throw new DemoScenarioConflictException(
                    $"At most {DemoScenarioLimits.MaxActiveScenarios} demo scenarios may run at once.");
            }

            existing?.Cancellation.Dispose();
            var execution = new ScenarioExecution(
                $"demo-{Guid.NewGuid():N}",
                name,
                DateTimeOffset.UtcNow,
                maximumDuration,
                new CancellationTokenSource());
            _executions[name] = execution;
            return execution;
        }
    }

    private async Task RunCoreAsync(
        ScenarioExecution execution,
        Func<CancellationToken, Task> workload)
    {
        lock (_gate)
        {
            execution.State = "running";
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(execution.Cancellation.Token);
        timeout.CancelAfter(execution.MaximumDuration);

        try
        {
            logger.LogInformation(
                "Demo scenario {ScenarioName} started with id {ScenarioId} and maximum duration {MaximumDurationMs} ms.",
                execution.Name,
                execution.Id,
                execution.MaximumDuration.TotalMilliseconds);
            await workload(timeout.Token);
            lock (_gate)
            {
                execution.State = "completed";
                execution.Message = "Scenario completed within its configured bound.";
            }
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            lock (_gate)
            {
                execution.State = execution.Cancellation.IsCancellationRequested ? "cancelled" : "completed";
                execution.Message = execution.Cancellation.IsCancellationRequested
                    ? "Scenario was cancelled by reset."
                    : "Scenario reached its configured duration bound.";
            }
        }
        catch (Exception exception)
        {
            lock (_gate)
            {
                execution.State = "failed";
                execution.Message = "Scenario failed; inspect the structured error log.";
            }
            logger.LogError(exception, "Demo scenario {ScenarioName} failed.", execution.Name);
        }
        finally
        {
            lock (_gate)
            {
                execution.CompletedAt = DateTimeOffset.UtcNow;
            }
            logger.LogInformation(
                "Demo scenario {ScenarioName} finished with state {ScenarioState} and id {ScenarioId}.",
                execution.Name,
                execution.State,
                execution.Id);
        }
    }

    private static bool IsActive(ScenarioExecution execution)
    {
        return execution.State is "queued" or "running";
    }

    private static DemoScenarioSnapshot Snapshot(ScenarioExecution execution)
    {
        return new DemoScenarioSnapshot(
            execution.Id,
            execution.Name,
            execution.State,
            execution.StartedAt,
            execution.CompletedAt,
            (int)Math.Ceiling(execution.MaximumDuration.TotalSeconds),
            execution.Message);
    }

    private sealed class ScenarioExecution(
        string id,
        string name,
        DateTimeOffset startedAt,
        TimeSpan maximumDuration,
        CancellationTokenSource cancellation)
    {
        public string Id { get; } = id;
        public string Name { get; } = name;
        public DateTimeOffset StartedAt { get; } = startedAt;
        public TimeSpan MaximumDuration { get; } = maximumDuration;
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public Task Task { get; set; } = Task.CompletedTask;
        public string State { get; set; } = "queued";
        public DateTimeOffset? CompletedAt { get; set; }
        public string? Message { get; set; }
    }
}

public sealed record DemoScenarioSnapshot(
    string Id,
    string Name,
    string State,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    int MaximumDurationSeconds,
    string? Message);

public sealed record DemoStatusResponse(
    int ActiveCount,
    IReadOnlyList<DemoScenarioSnapshot> Scenarios,
    object Limits);

public sealed class DemoScenarioConflictException(string message) : Exception(message);
