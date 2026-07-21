using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Tracebag.Api.Data;

public sealed class TracebagDbContextFactory : IDesignTimeDbContextFactory<TracebagDbContext>
{
    public TracebagDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("TRACEBAG_DATABASE_URL")
            ?? "Host=localhost;Port=5432;Database=tracebag;Username=tracebag;Password=tracebag";

        var options = new DbContextOptionsBuilder<TracebagDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new TracebagDbContext(options);
    }
}
