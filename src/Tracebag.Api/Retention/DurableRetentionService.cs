namespace Tracebag.Api.Retention;

public sealed class DurableRetentionService(
    DurableRetentionStore retention,
    Tracebag.Api.Auth.TracebagOptions options,
    ILogger<DurableRetentionService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await retention.ApplyAsync(stoppingToken);
                if (result.DeletedJobs > 0)
                {
                    logger.LogInformation(
                        "Durable retention deleted {DeletedJobs} expired diagnostic jobs and their events.",
                        result.DeletedJobs);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                retention.RecordFailure(exception);
                logger.LogWarning(exception, "Durable retention failed; preserved data will be retried later.");
            }

            await Task.Delay(TimeSpan.FromSeconds(options.DurableRetentionScanSeconds), stoppingToken);
        }
    }
}
