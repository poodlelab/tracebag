using Microsoft.Extensions.Logging.Abstractions;
using Tracebag.Demo.Api.Scenarios;

namespace Tracebag.DemoTests;

public sealed class DemoScenarioCoordinatorTests
{
    [Fact]
    public async Task CompletedScenarioLeavesNoActiveWork()
    {
        var coordinator = CreateCoordinator();

        coordinator.Start("quick", TimeSpan.FromSeconds(1), _ => Task.CompletedTask);
        await WaitForStateAsync(coordinator, "completed");

        Assert.Equal(0, coordinator.Status().ActiveCount);
    }

    [Fact]
    public async Task ResetCancelsActiveScenario()
    {
        var coordinator = CreateCoordinator();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.Start("blocking", TimeSpan.FromMinutes(1), async cancellationToken =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        });
        await started.Task.WaitAsync(TimeSpan.FromSeconds(1));

        var status = await coordinator.ResetAsync(CancellationToken.None);

        Assert.Equal(0, status.ActiveCount);
        Assert.Equal("cancelled", Assert.Single(status.Scenarios).State);
    }

    [Fact]
    public async Task DuplicateActiveScenarioIsRejected()
    {
        var coordinator = CreateCoordinator();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.Start("cpu", TimeSpan.FromMinutes(1), async cancellationToken =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        });
        await started.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Throws<DemoScenarioConflictException>(
            () => coordinator.Start("cpu", TimeSpan.FromSeconds(1), _ => Task.CompletedTask));

        await coordinator.ResetAsync(CancellationToken.None);
    }

    private static DemoScenarioCoordinator CreateCoordinator()
    {
        return new DemoScenarioCoordinator(NullLogger<DemoScenarioCoordinator>.Instance);
    }

    private static async Task WaitForStateAsync(DemoScenarioCoordinator coordinator, string state)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (coordinator.Status().Scenarios.SingleOrDefault()?.State == state)
            {
                return;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException($"Scenario did not reach {state}.");
    }
}
