using System.Globalization;
using Docker.DotNet.Models;
using Tracebag.Api.Auth;
using Tracebag.Api.Models;
using Tracebag.Api.Security;

namespace Tracebag.Api.Diagnostics;

public sealed class DiagnosticJobProfileCatalog(TracebagOptions options)
{
    public const string FullDumpConfirmation = "I understand this full dump may contain secrets and personal data";

    private static readonly Dictionary<string, ProfileDefinition> Profiles =
        new Dictionary<string, ProfileDefinition>(StringComparer.Ordinal)
        {
            ["stack-snapshot"] = new("Stack snapshot", "Capture managed stacks for a hung or starved process.", DiagnosticRunnerOperation.StackSnapshot, "stack", "txt", 0, 30, false),
            ["cpu-trace"] = new("CPU trace", "Sample CPU hot paths with a bounded EventPipe trace.", DiagnosticRunnerOperation.CpuTrace, "trace-cpu", "nettrace", 30, 120, false),
            ["threading-trace"] = new("Threading trace", "Capture thread and thread-pool scheduling events.", DiagnosticRunnerOperation.ThreadingTrace, "trace-threading", "nettrace", 30, 120, false),
            ["contention-trace"] = new("Contention trace", "Capture managed lock-contention events.", DiagnosticRunnerOperation.ContentionTrace, "trace-contention", "nettrace", 30, 120, false),
            ["gc-dump"] = new("GC dump", "Capture the managed heap graph without process memory pages.", DiagnosticRunnerOperation.GcDump, "gcdump", "gcdump", 0, 120, false),
            ["full-dump"] = new("Full process dump", "Capture all process memory. This can contain secrets and personal data.", DiagnosticRunnerOperation.FullDump, "dump-full", "dmp", 0, 300, true)
        };

    public IReadOnlyList<DiagnosticJobProfileResponse> List() => Profiles.Select(pair => new DiagnosticJobProfileResponse(
        pair.Key,
        pair.Value.DisplayName,
        pair.Value.Description,
        pair.Value.DefaultDurationSeconds,
        Math.Min(pair.Value.MaxDurationSeconds, options.DiagnosticJobMaxDurationSeconds),
        pair.Value.Sensitive,
        !pair.Value.Sensitive || options.FullDumpEnabled)).ToArray();

    public ResolvedDiagnosticProfile Resolve(
        DiagnosticJobCreateRequest request,
        ContainerListResponse container,
        string outputFileName)
    {
        var profileId = request.Profile?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!Profiles.TryGetValue(profileId, out var profile))
        {
            throw new TracebagException(StatusCodes.Status400BadRequest, "diagnostic_profile_invalid", "The requested diagnostic profile is not allowed.");
        }

        if (request.ProcessId <= 0)
        {
            throw new TracebagException(StatusCodes.Status400BadRequest, "process_id_invalid", "A valid process id is required.");
        }

        if (profile.Sensitive)
        {
            if (!options.FullDumpEnabled)
            {
                throw new TracebagException(StatusCodes.Status403Forbidden, "full_dump_globally_disabled", "Full process dumps are disabled for this Tracebag installation.");
            }

            var labels = container.Labels ?? new Dictionary<string, string>();
            if (!labels.TryGetValue("tracebag.diagnostics.fullDump", out var targetValue)
                || !string.Equals(targetValue, "true", StringComparison.OrdinalIgnoreCase))
            {
                throw new TracebagException(StatusCodes.Status403Forbidden, "full_dump_target_disabled", "The target container has not opted in to full process dumps.");
            }

            if (!string.Equals(request.Confirmation, FullDumpConfirmation, StringComparison.Ordinal))
            {
                throw new TracebagException(StatusCodes.Status400BadRequest, "full_dump_confirmation_required", "Full process dumps require the exact confirmation text.");
            }
        }

        var duration = profile.DefaultDurationSeconds == 0
            ? 0
            : Math.Clamp(request.DurationSeconds ?? profile.DefaultDurationSeconds, 1, Math.Min(profile.MaxDurationSeconds, options.DiagnosticJobMaxDurationSeconds));
        var timeout = Math.Min(options.DiagnosticJobMaxDurationSeconds, Math.Max(profile.MaxDurationSeconds, duration + 30));
        var pid = request.ProcessId.ToString(CultureInfo.InvariantCulture);
        var command = profile.RunnerCommand.StartsWith("trace-", StringComparison.Ordinal)
            ? new[] { profile.RunnerCommand, pid, TimeSpan.FromSeconds(duration).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture), outputFileName }
            : new[] { profile.RunnerCommand, pid, outputFileName };

        return new ResolvedDiagnosticProfile(profileId, profile.DisplayName, profile.Operation, profile.Extension, duration, timeout, profile.Sensitive, command);
    }

    private sealed record ProfileDefinition(
        string DisplayName,
        string Description,
        DiagnosticRunnerOperation Operation,
        string RunnerCommand,
        string Extension,
        int DefaultDurationSeconds,
        int MaxDurationSeconds,
        bool Sensitive);
}

public sealed record ResolvedDiagnosticProfile(
    string Id,
    string DisplayName,
    DiagnosticRunnerOperation Operation,
    string Extension,
    int DurationSeconds,
    int TimeoutSeconds,
    bool Sensitive,
    IReadOnlyList<string> Command);
