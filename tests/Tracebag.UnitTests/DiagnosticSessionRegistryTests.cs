using Tracebag.Api.Diagnostics;
using Tracebag.Api.Security;

namespace Tracebag.UnitTests;

public sealed class DiagnosticSessionRegistryTests
{
    [Fact]
    public void ReserveTargetBlocksSecondSessionBeforeRunnerStarts()
    {
        var registry = new DiagnosticSessionRegistry();
        using var reservation = registry.ReserveTarget("container-1");

        var ex = Assert.Throws<TracebagException>(() => registry.ReserveTarget("container-1"));

        Assert.Equal("counter_session_already_running", ex.Code);
    }

    [Fact]
    public void ReservationIsReleasedWhenRunnerCreationFails()
    {
        var registry = new DiagnosticSessionRegistry();
        registry.ReserveTarget("container-1").Dispose();

        using var reservation = registry.ReserveTarget("container-1");

        Assert.NotNull(reservation);
    }

    [Fact]
    public void AddedSessionRemainsActiveAfterReservationIsDisposed()
    {
        var registry = new DiagnosticSessionRegistry();
        using (registry.ReserveTarget("container-1"))
        {
            registry.Add(new DiagnosticSession(
                "session-1",
                "container-1",
                "api",
                "runner-1",
                "runner-name",
                DateTimeOffset.UtcNow,
                "admin"));
        }

        var ex = Assert.Throws<TracebagException>(() => registry.ReserveTarget("container-1"));

        Assert.Equal("counter_session_already_running", ex.Code);
        Assert.True(registry.Remove("session-1", out _));
    }
}
