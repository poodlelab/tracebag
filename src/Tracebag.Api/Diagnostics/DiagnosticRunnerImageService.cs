using System.Collections.Concurrent;
using Docker.DotNet.Models;
using Tracebag.Api.Docker;
using Tracebag.Api.Security;

namespace Tracebag.Api.Diagnostics;

public sealed class DiagnosticRunnerImageService
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _imageLocks = new(StringComparer.Ordinal);
    private readonly DockerClientFactory _docker;
    private readonly ILogger<DiagnosticRunnerImageService> _logger;

    public DiagnosticRunnerImageService(
        DockerClientFactory docker,
        ILogger<DiagnosticRunnerImageService> logger)
    {
        _docker = docker;
        _logger = logger;
    }

    public async Task EnsureAvailableAsync(
        DiagnosticRunnerSelection runner,
        CancellationToken cancellationToken)
    {
        if (await IsAvailableAsync(runner.Image, cancellationToken))
        {
            return;
        }

        var imageLock = _imageLocks.GetOrAdd(runner.Image, _ => new SemaphoreSlim(1, 1));
        await imageLock.WaitAsync(cancellationToken);
        try
        {
            if (await IsAvailableAsync(runner.Image, cancellationToken))
            {
                return;
            }

            _logger.LogInformation(
                "Pulling the configured .NET {RuntimeMajor} diagnostic runner image {Image} on first use.",
                runner.RuntimeMajor,
                runner.Image);

            string? pullError = null;
            var progress = new InlineProgress<JSONMessage>(message =>
            {
                if (!string.IsNullOrWhiteSpace(message.ErrorMessage))
                {
                    pullError = message.ErrorMessage;
                }
            });

            try
            {
                await _docker.Client.Images.CreateImageAsync(
                    new ImagesCreateParameters { FromImage = runner.Image },
                    null,
                    progress,
                    cancellationToken);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(exception, "The diagnostic runner image pull failed for {Image}.", runner.Image);
                throw PullFailed(runner);
            }

            if (!await IsAvailableAsync(runner.Image, cancellationToken))
            {
                _logger.LogWarning(
                    "Docker completed the diagnostic runner pull for {Image}, but the image is still unavailable. Detail: {Detail}",
                    runner.Image,
                    pullError ?? "none");
                throw PullFailed(runner);
            }
        }
        finally
        {
            imageLock.Release();
        }
    }

    private async Task<bool> IsAvailableAsync(string image, CancellationToken cancellationToken)
    {
        try
        {
            await _docker.Client.Images.InspectImageAsync(image, cancellationToken);
            return true;
        }
        catch (global::Docker.DotNet.DockerImageNotFoundException)
        {
            return false;
        }
    }

    private static TracebagException PullFailed(DiagnosticRunnerSelection runner)
    {
        return new TracebagException(
            StatusCodes.Status503ServiceUnavailable,
            "runner_image_pull_failed",
            $"Tracebag could not download the configured .NET {runner.RuntimeMajor} diagnostic runner image. " +
            $"Verify Docker registry access or pre-pull '{runner.Image}'.");
    }
}
