namespace Tracebag.Api.Diagnostics;

public sealed class DiagnosticJobRecoveryService(
    DiagnosticJobService jobs,
    ILogger<DiagnosticJobRecoveryService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try { await jobs.RecoverAfterRestartAsync(cancellationToken); }
        catch (Exception ex) { logger.LogWarning(ex, "Diagnostic job and artifact reconciliation failed."); }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
