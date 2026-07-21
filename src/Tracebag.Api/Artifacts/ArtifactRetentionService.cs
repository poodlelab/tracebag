namespace Tracebag.Api.Artifacts;

public sealed class ArtifactRetentionService : BackgroundService
{
    private readonly ArtifactStore _artifactStore;
    private readonly ILogger<ArtifactRetentionService> _logger;

    public ArtifactRetentionService(ArtifactStore artifactStore, ILogger<ArtifactRetentionService> logger)
    {
        _artifactStore = artifactStore;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _artifactStore.ApplyRetentionAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Artifact retention failed.");
            }

            await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
        }
    }
}
