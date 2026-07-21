using Docker.DotNet.Models;
using Tracebag.Api.Auth;
using Tracebag.Api.Diagnostics;
using Tracebag.Api.Docker;

namespace Tracebag.UnitTests;

public sealed class DiagnosticRunnerContainerPolicyTests
{
    [Theory]
    [InlineData(DiagnosticRunnerOperation.ProcessDiscovery, "processes", false)]
    [InlineData(DiagnosticRunnerOperation.LiveCounters, "counter-loop", false)]
    [InlineData(DiagnosticRunnerOperation.CounterRecording, "counter-recording", false)]
    [InlineData(DiagnosticRunnerOperation.StackSnapshot, "stack", true)]
    [InlineData(DiagnosticRunnerOperation.CpuTrace, "trace-cpu", true)]
    [InlineData(DiagnosticRunnerOperation.ThreadingTrace, "trace-threading", true)]
    [InlineData(DiagnosticRunnerOperation.ContentionTrace, "trace-contention", true)]
    [InlineData(DiagnosticRunnerOperation.GcDump, "gcdump", true)]
    [InlineData(DiagnosticRunnerOperation.FullDump, "dump-full", true)]
    public void AppliesOneHardenedBaselineToEveryOperation(
        DiagnosticRunnerOperation operation,
        string command,
        bool expectsArtifacts)
    {
        var options = Options();
        var policy = new DiagnosticRunnerContainerPolicy(options, new ContainerPolicy(options));

        var parameters = policy.Build(Request(operation, command, expectsArtifacts));

        Assert.Equal("runner:8", parameters.Image);
        Assert.True(parameters.AttachStdout);
        Assert.True(parameters.AttachStderr);
        Assert.False(parameters.Tty);
        Assert.Equal("container:target-id", parameters.HostConfig.PidMode);
        Assert.Equal("none", parameters.HostConfig.NetworkMode);
        Assert.True(parameters.HostConfig.Init);
        Assert.False(parameters.HostConfig.AutoRemove);
        Assert.True(parameters.HostConfig.ReadonlyRootfs);
        Assert.Equal(["ALL"], parameters.HostConfig.CapDrop);
        Assert.Equal(["no-new-privileges:true"], parameters.HostConfig.SecurityOpt);
        Assert.Equal(options.RunnerMemoryLimitBytes, parameters.HostConfig.Memory);
        Assert.Equal(options.RunnerMemoryLimitBytes, parameters.HostConfig.MemorySwap);
        Assert.Equal(options.RunnerCpuLimitMillicores * 1_000_000L, parameters.HostConfig.NanoCPUs);
        Assert.Equal(options.RunnerPidsLimit, parameters.HostConfig.PidsLimit);
        Assert.Equal(expectsArtifacts ? 2 : 1, parameters.HostConfig.Mounts.Count);
        Assert.Contains(parameters.HostConfig.Mounts, mount => mount.Source == "target-tmp" && mount.Target == "/tmp" && !mount.ReadOnly);
        Assert.Equal(expectsArtifacts, parameters.HostConfig.Mounts.Any(mount => mount.Source == "artifacts" && mount.Target == "/artifacts" && !mount.ReadOnly));
        Assert.Equal("true", parameters.Labels["tracebag.runner"]);
        Assert.Equal("true", parameters.Labels["tracebag.internal"]);
        Assert.False(parameters.Labels.ContainsKey("tracebag.profile") && !expectsArtifacts);
    }

    [Fact]
    public void AppliesOwnerSpecificLabelsWithoutAllowingPolicyDrift()
    {
        var options = Options();
        var policy = new DiagnosticRunnerContainerPolicy(options, new ContainerPolicy(options));

        var session = policy.Build(Request(DiagnosticRunnerOperation.LiveCounters, "counter-loop", false));
        var recording = policy.Build(Request(DiagnosticRunnerOperation.CounterRecording, "counter-recording", false));
        var job = policy.Build(Request(DiagnosticRunnerOperation.CpuTrace, "trace-cpu", true));

        Assert.Equal("owner-id", session.Labels["tracebag.sessionId"]);
        Assert.Equal("true", recording.Labels["tracebag.recording"]);
        Assert.Equal("owner-id", recording.Labels["tracebag.recordingId"]);
        Assert.Equal("true", job.Labels["tracebag.diagnosticJob"]);
        Assert.Equal("owner-id", job.Labels["tracebag.diagnosticJobId"]);
        Assert.Equal("cpu-trace", job.Labels["tracebag.profile"]);
    }

    [Fact]
    public void RejectsACommandThatDoesNotMatchTheFixedOperation()
    {
        var options = Options();
        var policy = new DiagnosticRunnerContainerPolicy(options, new ContainerPolicy(options));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            policy.Build(Request(DiagnosticRunnerOperation.ProcessDiscovery, "counter-loop", false)));

        Assert.Contains("fixed operation policy", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsArtifactProfilesForTransientRunnersAndMissingProfilesForJobs()
    {
        var options = Options();
        var policy = new DiagnosticRunnerContainerPolicy(options, new ContainerPolicy(options));

        Assert.Throws<InvalidOperationException>(() => policy.Build(
            Request(DiagnosticRunnerOperation.LiveCounters, "counter-loop", true) with { ProfileId = "cpu-trace" }));
        Assert.Throws<InvalidOperationException>(() => policy.Build(
            Request(DiagnosticRunnerOperation.CpuTrace, "trace-cpu", true) with { ProfileId = null }));
    }

    private static DiagnosticRunnerContainerRequest Request(
        DiagnosticRunnerOperation operation,
        string command,
        bool durable) => new(
            new ContainerListResponse
            {
                ID = "target-id",
                Labels = new Dictionary<string, string>
                {
                    ["tracebag.dotnet.tmpVolume"] = "target-tmp"
                }
            },
            new DiagnosticRunnerSelection(8, "runner:8", "tool-version", true),
            operation,
            "owner-id",
            "tracebag-runner-test-owner-id",
            [command],
            durable ? "cpu-trace" : null);

    private static TracebagOptions Options() => new()
    {
        Stage = "test",
        AuthEnabled = true,
        AdminUser = "admin",
        AdminPasswordHash = "hash",
        AllowedLabelKey = "tracebag.enabled",
        AllowedLabelValue = "true",
        ArtifactDir = "/tmp/artifacts",
        DataDir = "/tmp/data",
        ArtifactVolume = "artifacts",
        DiagnosticImage = "runner:8",
        PublicBaseUrl = "http://localhost",
        RestartEnabled = false,
        CounterMaxSeconds = 600,
        ArtifactRetentionHours = 24,
        ArtifactMaxCount = 20,
        ArtifactMaxTotalBytes = 1024 * 1024,
        RunnerMemoryLimitBytes = 805_306_368,
        RunnerCpuLimitMillicores = 750,
        RunnerPidsLimit = 96
    };
}
