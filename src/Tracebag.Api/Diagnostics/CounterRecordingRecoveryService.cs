namespace Tracebag.Api.Diagnostics;

public sealed class CounterRecordingRecoveryService(
    CounterRecordingService recordingService,
    ILogger<CounterRecordingRecoveryService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await recordingService.RecoverAfterRestartAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Counter recording recovery failed.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
