using Docker.DotNet.Models;
using Tracebag.Api.Docker;

namespace Tracebag.UnitTests;

public sealed class ContainerOperationalServiceTests
{
    [Fact]
    public void CalculatesResourceSnapshotFromDockerCounters()
    {
        var stats = new ContainerStatsResponse
        {
            Read = new DateTime(2026, 7, 20, 12, 0, 0, DateTimeKind.Utc),
            CPUStats = new CPUStats
            {
                SystemUsage = 1_000,
                OnlineCPUs = 2,
                CPUUsage = new CPUUsage { TotalUsage = 300 }
            },
            PreCPUStats = new CPUStats
            {
                SystemUsage = 500,
                CPUUsage = new CPUUsage { TotalUsage = 100 }
            },
            MemoryStats = new MemoryStats
            {
                Usage = 1_000,
                Limit = 2_000,
                Stats = new Dictionary<string, ulong> { ["inactive_file"] = 100 }
            },
            Networks = new Dictionary<string, NetworkStats>
            {
                ["eth0"] = new() { RxBytes = 10, TxBytes = 20 },
                ["eth1"] = new() { RxBytes = 30, TxBytes = 40 }
            },
            BlkioStats = new BlkioStats
            {
                IoServiceBytesRecursive =
                [
                    new BlkioStatEntry { Op = "Read", Value = 50 },
                    new BlkioStatEntry { Op = "Write", Value = 70 }
                ]
            },
            PidsStats = new PidsStats { Current = 7 }
        };

        var result = ContainerOperationalService.ToStatsDto(stats);

        Assert.True(result.Available);
        Assert.Equal(80, result.CpuPercent);
        Assert.Equal((ulong)900, result.MemoryUsageBytes);
        Assert.Equal(45, result.MemoryPercent);
        Assert.Equal((ulong)40, result.NetworkRxBytes);
        Assert.Equal((ulong)60, result.NetworkTxBytes);
        Assert.Equal((ulong)50, result.BlockReadBytes);
        Assert.Equal((ulong)70, result.BlockWriteBytes);
        Assert.Equal((ulong)7, result.Pids);
    }
}
