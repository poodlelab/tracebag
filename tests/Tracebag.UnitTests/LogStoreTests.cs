using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Globalization;
using Tracebag.Api.Auth;
using Tracebag.Api.Data;
using Tracebag.Api.Logs;
using Tracebag.Api.Models;

namespace Tracebag.UnitTests;

public sealed class LogStoreTests
{
    [Fact]
    public async Task PersistsCheckpointAndDeduplicatesInclusiveResume()
    {
        var fixture = Fixture();
        var parser = new LogParserChain();
        var target = Target("docker-one");
        var pending = PendingLogEntry.Create(
            target,
            "stdout",
            "2026-07-20T18:00:00.123456789Z {\"LogLevel\":\"Information\",\"Message\":\"known marker\"}",
            DateTimeOffset.UtcNow,
            parser);

        var first = await fixture.Store.PersistBatchAsync([pending], CancellationToken.None);
        var replay = await fixture.Store.PersistBatchAsync([pending], CancellationToken.None);
        var resume = await fixture.Store.GetResumeTimestampAsync(target.ContainerId, target.DockerId, CancellationToken.None);

        Assert.Single(first.Entries);
        Assert.Empty(replay.Entries);
        Assert.Equal(1, replay.DuplicateCount);
        Assert.NotNull(resume);
        await using var db = await fixture.Factory.CreateDbContextAsync();
        Assert.Single(await db.LogEntries.ToListAsync());
        Assert.Single(await db.LogCheckpoints.ToListAsync());
    }

    [Fact]
    public async Task SearchesByTextLevelTimeAndCursorAcrossRecreation()
    {
        var fixture = Fixture();
        var parser = new LogParserChain();
        var first = PendingLogEntry.Create(
            Target("docker-one"), "stdout",
            "2026-07-20T18:00:00Z {\"LogLevel\":\"Information\",\"Message\":\"known marker one\"}",
            DateTimeOffset.UtcNow, parser);
        var second = PendingLogEntry.Create(
            Target("docker-two"), "stderr",
            "2026-07-20T18:01:00Z {\"LogLevel\":\"Error\",\"Message\":\"known marker two\",\"ExceptionType\":\"System.TimeoutException\"}",
            DateTimeOffset.UtcNow, parser);
        await fixture.Store.PersistBatchAsync([first, second], CancellationToken.None);

        var page = await fixture.Store.SearchAsync(
            "compose:demo:api:1",
            new LogSearchRequest("known marker", "error", null, true, null,
                DateTimeOffset.Parse("2026-07-20T18:00:30Z", CultureInfo.InvariantCulture), null, null, 1),
            CancellationToken.None);

        var entry = Assert.Single(page.Items);
        Assert.Equal("docker-two", entry.DockerId);
        Assert.Equal("System.TimeoutException", entry.ExceptionType);

        var allFirstPage = await fixture.Store.SearchAsync(
            "compose:demo:api:1",
            new LogSearchRequest(null, null, null, Limit: 1),
            CancellationToken.None);
        var allSecondPage = await fixture.Store.SearchAsync(
            "compose:demo:api:1",
            new LogSearchRequest(null, null, null, Cursor: allFirstPage.NextCursor, Limit: 1),
            CancellationToken.None);

        Assert.True(allFirstPage.HasMore);
        Assert.Single(allSecondPage.Items);
        Assert.NotEqual(allFirstPage.Items[0].Id, allSecondPage.Items[0].Id);
    }

    [Fact]
    public async Task RetentionDeletesOnlyOneConfiguredBatchPerRule()
    {
        var dbOptions = new DbContextOptionsBuilder<TracebagDbContext>()
            .UseInMemoryDatabase($"tracebag-log-retention-{Guid.NewGuid():N}")
            .Options;
        var factory = new TestDbContextFactory(dbOptions);
        var store = new LogStore(Options() with { LogRetentionDeleteBatchSize = 2 }, factory);
        var parser = new LogParserChain();
        var target = Target("docker-old") with { RetentionDays = 1 };
        var entries = Enumerable.Range(1, 3)
            .Select(index => PendingLogEntry.Create(
                target,
                "stdout",
                $"2020-01-01T00:00:0{index}Z old {index}",
                DateTimeOffset.UtcNow,
                parser))
            .ToArray();
        await store.PersistBatchAsync(entries, CancellationToken.None);

        var deleted = await store.ApplyRetentionPassAsync(CancellationToken.None);

        Assert.Equal(2, deleted);
        await using var db = await factory.CreateDbContextAsync();
        Assert.Single(await db.LogEntries.ToListAsync());
    }

    private static (LogStore Store, TestDbContextFactory Factory) Fixture()
    {
        var dbOptions = new DbContextOptionsBuilder<TracebagDbContext>()
            .UseInMemoryDatabase($"tracebag-logs-{Guid.NewGuid():N}")
            .Options;
        var factory = new TestDbContextFactory(dbOptions);
        return (new LogStore(Options(), factory), factory);
    }

    private static LogTarget Target(string dockerId)
    {
        return new LogTarget(
            "compose:demo:api:1", dockerId, "demo-api", "demo:test", "auto", 7, 2_000_000,
            DateTimeOffset.Parse("2026-07-20T17:00:00Z", CultureInfo.InvariantCulture));
    }

    private static TracebagOptions Options()
    {
        return TracebagOptions.FromConfiguration(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TRACEBAG_AUTH_ENABLED"] = "false",
                ["TRACEBAG_DATABASE_URL"] = "in-memory",
                ["TRACEBAG_LOG_MAX_TOTAL_BYTES"] = "4000000",
                ["TRACEBAG_LOG_MAX_BYTES_PER_CONTAINER"] = "2000000"
            })
            .Build());
    }

    private sealed class TestDbContextFactory(DbContextOptions<TracebagDbContext> options)
        : IDbContextFactory<TracebagDbContext>
    {
        public TracebagDbContext CreateDbContext() => new(options);

        public Task<TracebagDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(CreateDbContext());
        }
    }
}
