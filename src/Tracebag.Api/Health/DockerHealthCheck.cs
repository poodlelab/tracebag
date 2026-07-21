using Microsoft.Extensions.Diagnostics.HealthChecks;
using Tracebag.Api.Docker;

namespace Tracebag.Api.Health;

public sealed class DockerHealthCheck(DockerClientFactory dockerClientFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await dockerClientFactory.Client.System.PingAsync(cancellationToken);
            return HealthCheckResult.Healthy("Docker Engine is reachable.");
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("Docker Engine is unreachable.");
        }
    }
}
