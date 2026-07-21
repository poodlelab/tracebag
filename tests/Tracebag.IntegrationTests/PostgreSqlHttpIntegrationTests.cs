using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tracebag.Api.Data;

namespace Tracebag.IntegrationTests;

public sealed class PostgreSqlHttpIntegrationTests
{
    [PostgreSqlFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task SearchAndIncidentDeletionUseRealMiddlewareAndRelationalSemantics()
    {
        var connectionString = Environment.GetEnvironmentVariable("TRACEBAG_TEST_DATABASE_URL")!;
        await using var factory = new PostgreSqlApplicationFactory(connectionString);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("http://localhost")
        });

        await factory.MigrateAndSeedAsync();

        var search = await client.GetAsync("/api/logs/search?containerId=target-one&text=deadlock&limit=20");
        Assert.Equal(HttpStatusCode.OK, search.StatusCode);
        var searchJson = await search.Content.ReadFromJsonAsync<JsonElement>();
        var item = Assert.Single(searchJson.GetProperty("items").EnumerateArray());
        Assert.Equal("A deadlock was detected in checkout", item.GetProperty("message").GetString());
        Assert.Equal("error", item.GetProperty("level").GetString());

        var wrongConfirmation = await client.DeleteAsync("/api/incidents/incident-one?confirm=wrong");
        Assert.Equal(HttpStatusCode.BadRequest, wrongConfirmation.StatusCode);
        Assert.Equal(
            "incident_delete_confirmation_required",
            (await wrongConfirmation.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());

        var deleted = await client.DeleteAsync("/api/incidents/incident-one?confirm=incident-one");
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        var deleteJson = await deleted.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, deleteJson.GetProperty("deletedEvidence").GetInt32());
        Assert.Equal(1, deleteJson.GetProperty("releasedDiagnosticJobs").GetInt32());

        await using var db = await factory.CreateDbContextAsync();
        Assert.False(await db.Incidents.AnyAsync(item => item.Id == "incident-one"));
        Assert.False(await db.IncidentEvidence.AnyAsync(item => item.IncidentId == "incident-one"));
        Assert.True(await db.DiagnosticJobs.AnyAsync(item => item.Id == "job-one"));
    }

    private sealed class PostgreSqlApplicationFactory(string connectionString) : WebApplicationFactory<Program>, IAsyncDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"tracebag-postgres-http-{Guid.NewGuid():N}");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("TRACEBAG_STAGE", "local");
            builder.UseSetting("TRACEBAG_AUTH_ENABLED", "false");
            builder.UseSetting("TRACEBAG_DATABASE_URL", connectionString);
            builder.UseSetting("TRACEBAG_LOG_INGESTION_ENABLED", "false");
            builder.UseSetting("TRACEBAG_DATA_DIR", Path.Combine(_root, "data"));
            builder.UseSetting("TRACEBAG_ARTIFACT_DIR", Path.Combine(_root, "artifacts"));
            builder.ConfigureTestServices(services =>
            {
                foreach (var descriptor in services
                    .Where(descriptor => descriptor.ServiceType == typeof(IHostedService))
                    .ToArray())
                {
                    services.Remove(descriptor);
                }
            });
        }

        public async Task MigrateAndSeedAsync()
        {
            await using var db = await CreateDbContextAsync();
            await db.Database.MigrateAsync();
            var now = DateTimeOffset.UtcNow;
            var stream = new LogStreamRecord
            {
                ContainerId = "target-one",
                CurrentDockerId = "docker-one",
                ContainerName = "Checkout API",
                Image = "checkout:test",
                Parser = "json",
                RetentionDays = 7,
                MaxBytes = 1_000_000,
                Active = true,
                StartedAt = now
            };
            db.LogStreams.Add(stream);
            await db.SaveChangesAsync();
            db.LogEntries.Add(new LogEntryRecord
            {
                LogStreamId = stream.Id,
                ContainerId = "target-one",
                DockerId = "docker-one",
                ReceivedAt = now,
                Timestamp = now,
                SourceTimestamp = now.ToString("O"),
                Stream = "stderr",
                Line = "A deadlock was detected in checkout",
                Message = "A deadlock was detected in checkout",
                Level = "error",
                Fingerprint = "postgres-http-deadlock",
                SizeBytes = 36
            });
            db.DiagnosticJobs.Add(new DiagnosticJobRecord
            {
                Id = "job-one",
                ContainerId = "target-one",
                ContainerName = "Checkout API",
                DockerId = "docker-one",
                ProcessId = 42,
                Profile = "stack-snapshot",
                Status = "completed",
                Progress = 100,
                CreatedAt = now.AddMinutes(-2),
                CompletedAt = now.AddMinutes(-1),
                DeadlineAt = now.AddMinutes(5),
                CreatedBy = "integration-test",
                RequestFingerprint = "postgres-http-job",
                RuntimeMajor = 8,
                RunnerImage = "runner:test",
                ToolVersion = "test"
            });
            db.Incidents.Add(new IncidentRecord
            {
                Id = "incident-one",
                ContainerId = "target-one",
                ContainerName = "Checkout API",
                DockerId = "docker-one",
                ProcessId = 42,
                Title = "Checkout deadlock",
                Profile = "contention",
                Status = "closed",
                Progress = 100,
                CreatedBy = "integration-test",
                CreatedAt = now.AddMinutes(-2),
                WindowStart = now.AddMinutes(-3),
                WindowEnd = now,
                CompletedAt = now,
                CaptureOptionsJson = "{}"
            });
            db.IncidentEvidence.Add(new IncidentEvidenceRecord
            {
                Id = "evidence-one",
                IncidentId = "incident-one",
                Kind = "diagnostic-artifact",
                Title = "Stack snapshot",
                CapturedAt = now,
                SourceId = "job-one",
                SummaryJson = "{}",
                PayloadJson = "{}",
                SelectedByDefault = true,
                RedactionStatus = "not-required"
            });
            await db.SaveChangesAsync();
        }

        public async Task<TracebagDbContext> CreateDbContextAsync()
        {
            return await Services.GetRequiredService<IDbContextFactory<TracebagDbContext>>()
                .CreateDbContextAsync();
        }

        public new async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }

    private sealed class PostgreSqlFactAttribute : FactAttribute
    {
        public PostgreSqlFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TRACEBAG_TEST_DATABASE_URL")))
            {
                Skip = "Run through scripts/verify-http-postgres-runtime.sh.";
            }
        }
    }
}
