using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Tracebag.Api.Audit;
using Tracebag.Api.Auth;
using Tracebag.Api.Data;

namespace Tracebag.UnitTests;

public sealed class AuditRetentionTests
{
    [Fact]
    public async Task DeletesExpiredEventsAndPreservesRecentEvents()
    {
        var factory = CreateFactory();
        await AddEventsAsync(factory,
            (DateTimeOffset.UtcNow.AddDays(-31), "old"),
            (DateTimeOffset.UtcNow.AddDays(-1), "recent"));
        using var audit = new AuditLog(Options() with { AuditRetentionDays = 30 }, factory);

        var result = await audit.ApplyRetentionPassAsync(CancellationToken.None);

        Assert.Equal(1, result.ExpiredDeleted);
        Assert.Equal(0, result.OverflowDeleted);
        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal("recent", Assert.Single(await db.AuditEvents.ToArrayAsync()).Action);
    }

    [Fact]
    public async Task DeletesOldestEventsAboveConfiguredCap()
    {
        var factory = CreateFactory();
        await AddEventsAsync(factory,
            (DateTimeOffset.UtcNow.AddMinutes(-3), "first"),
            (DateTimeOffset.UtcNow.AddMinutes(-2), "second"),
            (DateTimeOffset.UtcNow.AddMinutes(-1), "third"));
        using var audit = new AuditLog(Options() with
        {
            AuditMaxEvents = 2,
            AuditRetentionDeleteBatchSize = 100
        }, factory);

        var result = await audit.ApplyRetentionPassAsync(CancellationToken.None);

        Assert.Equal(1, result.OverflowDeleted);
        await using var db = await factory.CreateDbContextAsync();
        var actions = await db.AuditEvents.OrderBy(entry => entry.Timestamp).Select(entry => entry.Action).ToArrayAsync();
        Assert.Equal(["second", "third"], actions);
    }

    [Fact]
    public async Task BoundsPersistedAuditFields()
    {
        var factory = CreateFactory();
        using var audit = new AuditLog(Options(), factory);

        await audit.WriteAsync(
            new string('u', 500),
            new string('a', 500),
            new string('i', 500),
            new string('n', 500),
            new string('r', 500),
            null,
            CancellationToken.None);

        await using var db = await factory.CreateDbContextAsync();
        var entry = Assert.Single(await db.AuditEvents.ToArrayAsync());
        Assert.Equal(160, entry.User.Length);
        Assert.Equal(120, entry.Action.Length);
        Assert.Equal(128, entry.TargetContainerId!.Length);
        Assert.Equal(200, entry.TargetContainerName!.Length);
        Assert.Equal(40, entry.Result.Length);
    }

    private static TestFactory CreateFactory()
    {
        var options = new DbContextOptionsBuilder<TracebagDbContext>()
            .UseInMemoryDatabase($"audit-{Guid.NewGuid():N}")
            .Options;
        return new TestFactory(options);
    }

    private static async Task AddEventsAsync(TestFactory factory, params (DateTimeOffset Timestamp, string Action)[] events)
    {
        await using var db = await factory.CreateDbContextAsync();
        foreach (var item in events)
        {
            db.AuditEvents.Add(new AuditEventRecord
            {
                Timestamp = item.Timestamp,
                User = "admin",
                Action = item.Action,
                Result = "success"
            });
        }
        await db.SaveChangesAsync();
    }

    private static TracebagOptions Options()
    {
        return TracebagOptions.FromConfiguration(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TRACEBAG_AUTH_ENABLED"] = "false"
            })
            .Build());
    }

    private sealed class TestFactory(DbContextOptions<TracebagDbContext> options) : IDbContextFactory<TracebagDbContext>
    {
        public TracebagDbContext CreateDbContext() => new(options);
        public Task<TracebagDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }
}
