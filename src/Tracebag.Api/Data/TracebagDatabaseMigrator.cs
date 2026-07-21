using Microsoft.EntityFrameworkCore;

namespace Tracebag.Api.Data;

public sealed class TracebagDatabaseMigrator(
    IDbContextFactory<TracebagDbContext> dbContextFactory,
    ILogger<TracebagDatabaseMigrator> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Applying Tracebag database migrations.");
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await db.Database.MigrateAsync(cancellationToken);
        logger.LogInformation("Tracebag database migrations applied.");
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
