using Docker.DotNet.Models;
using Microsoft.EntityFrameworkCore;
using Tracebag.Api.Data;
using Tracebag.Api.Docker;

namespace Tracebag.UnitTests;

public sealed class ContainerTargetRegistryTests
{
    [Fact]
    public async Task RecreationUpdatesCurrentInstanceWithoutCreatingNewTarget()
    {
        var dbOptions = new DbContextOptionsBuilder<TracebagDbContext>()
            .UseInMemoryDatabase($"tracebag-targets-{Guid.NewGuid():N}")
            .Options;
        var factory = new TestDbContextFactory(dbOptions);
        var resolver = new ContainerIdentityResolver();
        var policy = new ContainerPolicy(ContainerIdentityResolverTests.TestOptions(), resolver);
        var registry = new ContainerTargetRegistry(policy, factory);

        await registry.ReconcileAsync([Container("docker-first")], CancellationToken.None);
        await registry.ReconcileAsync([Container("docker-second")], CancellationToken.None);

        await using var db = await factory.CreateDbContextAsync();
        var target = Assert.Single(await db.ContainerTargets.ToListAsync());
        var instances = await db.ContainerInstances.OrderBy(instance => instance.DockerId).ToListAsync();
        Assert.Equal("compose:demo:api:1", target.Id);
        Assert.Equal("docker-second", target.CurrentDockerId);
        Assert.Equal(2, instances.Count);
        Assert.NotNull(instances.Single(instance => instance.DockerId == "docker-first").RemovedAt);
        Assert.Null(instances.Single(instance => instance.DockerId == "docker-second").RemovedAt);
    }

    private static ContainerListResponse Container(string dockerId)
    {
        return new ContainerListResponse
        {
            ID = dockerId,
            Image = "demo:test",
            State = "running",
            Status = "Up",
            Created = DateTime.UtcNow,
            Names = ["/demo-api-1"],
            Labels = new Dictionary<string, string>
            {
                ["tracebag.enabled"] = "true",
                ["tracebag.kind"] = "dotnet",
                ["com.docker.compose.project"] = "demo",
                ["com.docker.compose.service"] = "api",
                ["com.docker.compose.container-number"] = "1"
            }
        };
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
