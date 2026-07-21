using Microsoft.Extensions.Diagnostics.HealthChecks;
using Tracebag.Api.Auth;

namespace Tracebag.Api.Health;

public sealed class ArtifactStorageHealthCheck(TracebagOptions options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var probePath = Path.Combine(options.ArtifactDir, $".tracebag-health-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(options.ArtifactDir);
            await using var stream = new FileStream(
                probePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1,
                FileOptions.Asynchronous);
            await stream.WriteAsync(new byte[] { 0 }, cancellationToken);
            await stream.FlushAsync(cancellationToken);

            return HealthCheckResult.Healthy("Artifact storage is writable.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return HealthCheckResult.Unhealthy("Artifact storage is not writable.");
        }
        finally
        {
            try
            {
                File.Delete(probePath);
            }
            catch (IOException)
            {
                // The next retention pass can remove an abandoned probe file.
            }
            catch (UnauthorizedAccessException)
            {
                // The health result already reports that the storage is unusable.
            }
        }
    }
}
