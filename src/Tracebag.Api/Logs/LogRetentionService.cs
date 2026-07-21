using Tracebag.Api.Auth;

namespace Tracebag.Api.Logs;

public sealed class LogRetentionService(
    TracebagOptions options,
    LogStore logStore,
    LogIngestionCoordinator coordinator,
    ILogger<LogRetentionService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var deleted = await logStore.ApplyRetentionPassAsync(stoppingToken);
                coordinator.RecordRetentionDeletion(deleted);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Log retention pass failed; retrying later.");
            }

            await Task.Delay(TimeSpan.FromSeconds(options.LogRetentionScanSeconds), stoppingToken);
        }
    }
}
