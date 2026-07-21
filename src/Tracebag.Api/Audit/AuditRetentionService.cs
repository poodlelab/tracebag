namespace Tracebag.Api.Audit;

public sealed class AuditRetentionService(
    AuditLog auditLog,
    Tracebag.Api.Auth.TracebagOptions options,
    ILogger<AuditRetentionService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await auditLog.ApplyRetentionPassAsync(stoppingToken);
                if (result.ExpiredDeleted > 0 || result.OverflowDeleted > 0)
                {
                    logger.LogInformation(
                        "Audit retention deleted {ExpiredDeleted} expired and {OverflowDeleted} over-cap events.",
                        result.ExpiredDeleted,
                        result.OverflowDeleted);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Audit retention pass failed; retrying later.");
            }

            await Task.Delay(TimeSpan.FromSeconds(options.AuditRetentionScanSeconds), stoppingToken);
        }
    }
}
