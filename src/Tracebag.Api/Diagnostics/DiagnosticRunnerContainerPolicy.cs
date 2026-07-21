using System.Globalization;
using Docker.DotNet.Models;
using Tracebag.Api.Auth;
using Tracebag.Api.Docker;

namespace Tracebag.Api.Diagnostics;

public enum DiagnosticRunnerOperation
{
    ProcessDiscovery,
    LiveCounters,
    CounterRecording,
    StackSnapshot,
    CpuTrace,
    ThreadingTrace,
    ContentionTrace,
    GcDump,
    FullDump
}

public sealed record DiagnosticRunnerContainerRequest(
    ContainerListResponse TargetContainer,
    DiagnosticRunnerSelection Runner,
    DiagnosticRunnerOperation Operation,
    string OwnerId,
    string RunnerName,
    IReadOnlyList<string> Command,
    string? ProfileId = null);

public sealed class DiagnosticRunnerContainerPolicy(TracebagOptions options, ContainerPolicy containerPolicy)
{
    public CreateContainerParameters Build(DiagnosticRunnerContainerRequest request)
    {
        Validate(request);

        var labels = new Dictionary<string, string>
        {
            ["tracebag.runner"] = "true",
            ["tracebag.internal"] = "true",
            ["tracebag.targetContainer"] = request.TargetContainer.ID,
            ["tracebag.instance"] = options.Stage,
            ["tracebag.runnerOperation"] = OperationName(request.Operation),
            ["tracebag.runtimeMajor"] = request.Runner.RuntimeMajor.ToString(CultureInfo.InvariantCulture),
            ["tracebag.toolVersion"] = request.Runner.ToolVersion
        };

        switch (request.Operation)
        {
            case DiagnosticRunnerOperation.ProcessDiscovery:
            case DiagnosticRunnerOperation.LiveCounters:
                labels["tracebag.sessionId"] = request.OwnerId;
                break;
            case DiagnosticRunnerOperation.CounterRecording:
                labels["tracebag.recording"] = "true";
                labels["tracebag.recordingId"] = request.OwnerId;
                break;
            default:
                labels["tracebag.diagnosticJob"] = "true";
                labels["tracebag.diagnosticJobId"] = request.OwnerId;
                labels["tracebag.profile"] = request.ProfileId!;
                break;
        }

        var mounts = new List<Mount>
        {
            new()
            {
                Type = "volume",
                Source = containerPolicy.GetDotnetTmpVolume(request.TargetContainer),
                Target = "/tmp",
                ReadOnly = false
            }
        };
        if (RequiresArtifactVolume(request.Operation))
        {
            mounts.Add(new Mount
            {
                Type = "volume",
                Source = options.ArtifactVolume,
                Target = "/artifacts",
                ReadOnly = false
            });
        }

        return new CreateContainerParameters
        {
            Image = request.Runner.Image,
            Name = request.RunnerName,
            Cmd = request.Command.ToList(),
            AttachStdout = true,
            AttachStderr = true,
            Tty = false,
            Labels = labels,
            HostConfig = new HostConfig
            {
                PidMode = $"container:{request.TargetContainer.ID}",
                NetworkMode = "none",
                Init = true,
                AutoRemove = false,
                ReadonlyRootfs = true,
                CapDrop = ["ALL"],
                SecurityOpt = ["no-new-privileges:true"],
                Memory = options.RunnerMemoryLimitBytes,
                MemorySwap = options.RunnerMemoryLimitBytes,
                NanoCPUs = options.RunnerCpuLimitMillicores * 1_000_000L,
                PidsLimit = options.RunnerPidsLimit,
                Mounts = mounts
            }
        };
    }

    public static bool RequiresArtifactVolume(DiagnosticRunnerOperation operation) => operation is
        DiagnosticRunnerOperation.StackSnapshot or
        DiagnosticRunnerOperation.CpuTrace or
        DiagnosticRunnerOperation.ThreadingTrace or
        DiagnosticRunnerOperation.ContentionTrace or
        DiagnosticRunnerOperation.GcDump or
        DiagnosticRunnerOperation.FullDump;

    private static void Validate(DiagnosticRunnerContainerRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.OwnerId))
        {
            throw new InvalidOperationException("A server-owned runner owner id is required.");
        }
        if (string.IsNullOrWhiteSpace(request.RunnerName) || !request.RunnerName.StartsWith("tracebag-", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Diagnostic runner names must use the Tracebag-owned prefix.");
        }
        if (request.Command.Count == 0 || !string.Equals(request.Command[0], ExpectedCommand(request.Operation), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The runner command does not match its fixed operation policy.");
        }
        if (RequiresArtifactVolume(request.Operation) && string.IsNullOrWhiteSpace(request.ProfileId))
        {
            throw new InvalidOperationException("Durable diagnostic runners require a fixed profile id.");
        }
        if (!RequiresArtifactVolume(request.Operation) && request.ProfileId is not null)
        {
            throw new InvalidOperationException("Transient diagnostic runners cannot declare an artifact profile.");
        }
    }

    private static string ExpectedCommand(DiagnosticRunnerOperation operation) => operation switch
    {
        DiagnosticRunnerOperation.ProcessDiscovery => "processes",
        DiagnosticRunnerOperation.LiveCounters => "counter-loop",
        DiagnosticRunnerOperation.CounterRecording => "counter-recording",
        DiagnosticRunnerOperation.StackSnapshot => "stack",
        DiagnosticRunnerOperation.CpuTrace => "trace-cpu",
        DiagnosticRunnerOperation.ThreadingTrace => "trace-threading",
        DiagnosticRunnerOperation.ContentionTrace => "trace-contention",
        DiagnosticRunnerOperation.GcDump => "gcdump",
        DiagnosticRunnerOperation.FullDump => "dump-full",
        _ => throw new ArgumentOutOfRangeException(nameof(operation))
    };

    private static string OperationName(DiagnosticRunnerOperation operation) => operation switch
    {
        DiagnosticRunnerOperation.ProcessDiscovery => "process-discovery",
        DiagnosticRunnerOperation.LiveCounters => "live-counters",
        DiagnosticRunnerOperation.CounterRecording => "counter-recording",
        DiagnosticRunnerOperation.StackSnapshot => "stack-snapshot",
        DiagnosticRunnerOperation.CpuTrace => "cpu-trace",
        DiagnosticRunnerOperation.ThreadingTrace => "threading-trace",
        DiagnosticRunnerOperation.ContentionTrace => "contention-trace",
        DiagnosticRunnerOperation.GcDump => "gc-dump",
        DiagnosticRunnerOperation.FullDump => "full-dump",
        _ => throw new ArgumentOutOfRangeException(nameof(operation))
    };
}
