using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Tracebag.Api.Auth;
using Tracebag.Api.Data;

namespace Tracebag.Api.Health;

public sealed class DatabaseHealthCheck(
    IServiceProvider serviceProvider,
    TracebagOptions options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (!options.DatabaseEnabled)
        {
            return HealthCheckResult.Healthy("PostgreSQL is not configured; development fallback is active.");
        }

        try
        {
            var factory = serviceProvider.GetRequiredService<IDbContextFactory<TracebagDbContext>>();
            await using var database = await factory.CreateDbContextAsync(cancellationToken);
            return await database.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy("PostgreSQL is reachable.")
                : HealthCheckResult.Unhealthy("PostgreSQL is unreachable.");
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL is unreachable.");
        }
    }
}
