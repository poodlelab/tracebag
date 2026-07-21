using Docker.DotNet.Models;
using System.Globalization;
using Tracebag.Api.Auth;
using Tracebag.Api.Security;

namespace Tracebag.Api.Diagnostics;

public sealed class DiagnosticRunnerCatalog
{
    public const string RuntimeLabel = "tracebag.dotnet.runtime";
    private readonly TracebagOptions _options;

    public DiagnosticRunnerCatalog(TracebagOptions options)
    {
        _options = options;
    }

    public DiagnosticRunnerSelection Select(ContainerListResponse container)
    {
        var labels = container.Labels ?? new Dictionary<string, string>();
        var configured = labels.TryGetValue(RuntimeLabel, out var value) ? value?.Trim() : null;
        var runtimeMajor = string.IsNullOrWhiteSpace(configured)
            ? _options.DiagnosticDefaultRuntimeMajor
            : ParseRuntime(configured);

        return runtimeMajor switch
        {
            8 => new DiagnosticRunnerSelection(8, _options.DiagnosticImage, "9.0.661903", configured is not null),
            9 => new DiagnosticRunnerSelection(9, _options.DiagnosticImageDotnet9, "9.0.661903", configured is not null),
            10 => new DiagnosticRunnerSelection(10, _options.DiagnosticImageDotnet10, "9.0.661903", configured is not null),
            _ => throw Unsupported(runtimeMajor.ToString(CultureInfo.InvariantCulture))
        };
    }

    private static int ParseRuntime(string value)
    {
        var majorText = value.Split('.', 2, StringSplitOptions.TrimEntries)[0];
        return int.TryParse(majorText, out var major) && major is >= 8 and <= 10
            ? major
            : throw Unsupported(value);
    }

    private static TracebagException Unsupported(string runtime)
    {
        return new TracebagException(
            StatusCodes.Status400BadRequest,
            "dotnet_runtime_unsupported",
            $"The target declares unsupported .NET runtime '{runtime}'. Supported runtime majors are 8, 9, and 10.");
    }
}

public sealed record DiagnosticRunnerSelection(
    int RuntimeMajor,
    string Image,
    string ToolVersion,
    bool RuntimeWasExplicit);
