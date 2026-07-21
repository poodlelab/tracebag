using System.Diagnostics;
using System.Text.Json;
using Tracebag.Demo.Api;
using Tracebag.Demo.Api.Scenarios;

if (args.FirstOrDefault() == "traffic-generator")
{
    var target = args.Skip(1).FirstOrDefault() ?? "http://tracebag-demo-api:8080";
    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        cancellation.Cancel();
    };
    await TrafficGenerator.RunAsync(target, cancellation.Token);
    return;
}

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options => options.JsonWriterOptions = new JsonWriterOptions { Indented = false });
builder.Services.AddSingleton<DemoScenarioCoordinator>();
builder.Services.AddSingleton<IHostedService>(services => services.GetRequiredService<DemoScenarioCoordinator>());
builder.Services.AddSingleton<DemoWorkloads>();

var app = builder.Build();
var scenarioEndpoints = new[]
{
    "GET /demo/healthy",
    "POST /demo/cpu?seconds=20&workers=2",
    "POST /demo/allocations?seconds=20&mbPerSecond=20",
    "POST /demo/exceptions?count=20",
    "POST /demo/slow?milliseconds=3000",
    "POST /demo/lock-contention?seconds=20&workers=20",
    "POST /demo/threadpool-starvation?seconds=20&workers=24",
    "POST /demo/dependency-failure?seconds=20",
    "POST /demo/reset"
};

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/health"))
    {
        await next(context);
        return;
    }

    var stopwatch = Stopwatch.StartNew();
    try
    {
        await next(context);
    }
    finally
    {
        app.Logger.LogInformation(
            "Demo HTTP {Method} {Path} completed with {StatusCode} in {ElapsedMilliseconds} ms and trace {TraceId}.",
            context.Request.Method,
            context.Request.Path.Value,
            context.Response.StatusCode,
            stopwatch.Elapsed.TotalMilliseconds,
            context.TraceIdentifier);
    }
});

app.Use(async (context, next) =>
{
    try
    {
        await next(context);
    }
    catch (DemoScenarioValidationException exception)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new
        {
            error = "scenario_parameter_out_of_range",
            parameter = exception.Parameter,
            message = exception.Message
        });
    }
    catch (DemoScenarioConflictException exception)
    {
        context.Response.StatusCode = StatusCodes.Status409Conflict;
        await context.Response.WriteAsJsonAsync(new
        {
            error = "scenario_conflict",
            message = exception.Message
        });
    }
});

app.MapGet("/", () => Results.Ok(new
{
    product = "Tracebag Demo API",
    documentation = "/demo/status",
    scenarios = scenarioEndpoints
}));

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.MapGet("/demo/healthy", (HttpContext context, ILogger<Program> logger) =>
{
    var requestId = $"req-{Guid.NewGuid():N}";
    logger.LogInformation(
        "Demo healthy request {RequestId} processed for tier {CustomerTier} with {ItemCount} items and trace {TraceId}.",
        requestId,
        "gold",
        3,
        context.TraceIdentifier);
    return Results.Ok(new
    {
        status = "ok",
        requestId,
        traceId = context.TraceIdentifier,
        timestamp = DateTimeOffset.UtcNow
    });
});

app.MapPost("/demo/cpu", (
    int? seconds,
    int? workers,
    DemoScenarioCoordinator coordinator,
    DemoWorkloads workloads) =>
{
    var boundedSeconds = DemoScenarioLimits.CpuSeconds(seconds ?? 20);
    var boundedWorkers = DemoScenarioLimits.CpuWorkers(workers ?? 2);
    var scenario = coordinator.Start(
        "cpu",
        TimeSpan.FromSeconds(boundedSeconds + 2),
        cancellationToken => workloads.CpuAsync(boundedSeconds, boundedWorkers, cancellationToken));
    return Results.Accepted("/demo/status", new { scenario, seconds = boundedSeconds, workers = boundedWorkers });
});

app.MapPost("/demo/allocations", (
    int? seconds,
    int? mbPerSecond,
    DemoScenarioCoordinator coordinator,
    DemoWorkloads workloads) =>
{
    var boundedSeconds = DemoScenarioLimits.AllocationSeconds(seconds ?? 20);
    var boundedRate = DemoScenarioLimits.AllocationMegabytesPerSecond(mbPerSecond ?? 20);
    var scenario = coordinator.Start(
        "allocations",
        TimeSpan.FromSeconds(boundedSeconds + 2),
        cancellationToken => workloads.AllocationsAsync(boundedSeconds, boundedRate, cancellationToken));
    return Results.Accepted("/demo/status", new { scenario, seconds = boundedSeconds, mbPerSecond = boundedRate });
});

app.MapPost("/demo/exceptions", (
    int? count,
    DemoScenarioCoordinator coordinator,
    DemoWorkloads workloads) =>
{
    var boundedCount = DemoScenarioLimits.ExceptionCount(count ?? 20);
    var scenario = coordinator.Start(
        "exceptions",
        TimeSpan.FromSeconds(10),
        cancellationToken => workloads.ExceptionsAsync(boundedCount, cancellationToken));
    return Results.Accepted("/demo/status", new { scenario, count = boundedCount });
});

app.MapPost("/demo/slow", async (
    int? milliseconds,
    DemoScenarioCoordinator coordinator,
    DemoWorkloads workloads) =>
{
    var boundedMilliseconds = DemoScenarioLimits.SlowMilliseconds(milliseconds ?? 3_000);
    var scenario = await coordinator.RunInlineAsync(
        "slow-request",
        TimeSpan.FromMilliseconds(boundedMilliseconds + 1_000),
        cancellationToken => workloads.SlowRequestAsync(boundedMilliseconds, cancellationToken));
    return Results.Ok(new { scenario, milliseconds = boundedMilliseconds });
});

app.MapPost("/demo/lock-contention", (
    int? seconds,
    int? workers,
    DemoScenarioCoordinator coordinator,
    DemoWorkloads workloads) =>
{
    var boundedSeconds = DemoScenarioLimits.ContentionSeconds(seconds ?? 20);
    var boundedWorkers = DemoScenarioLimits.ContentionWorkers(workers ?? 20);
    var scenario = coordinator.Start(
        "lock-contention",
        TimeSpan.FromSeconds(boundedSeconds + 2),
        cancellationToken => workloads.LockContentionAsync(boundedSeconds, boundedWorkers, cancellationToken));
    return Results.Accepted("/demo/status", new { scenario, seconds = boundedSeconds, workers = boundedWorkers });
});

app.MapPost("/demo/threadpool-starvation", (
    int? seconds,
    int? workers,
    DemoScenarioCoordinator coordinator,
    DemoWorkloads workloads) =>
{
    var boundedSeconds = DemoScenarioLimits.StarvationSeconds(seconds ?? 20);
    var boundedWorkers = DemoScenarioLimits.StarvationWorkers(workers ?? 24);
    var scenario = coordinator.Start(
        "threadpool-starvation",
        TimeSpan.FromSeconds(boundedSeconds + 2),
        cancellationToken => workloads.ThreadPoolStarvationAsync(boundedSeconds, boundedWorkers, cancellationToken));
    return Results.Accepted("/demo/status", new { scenario, seconds = boundedSeconds, workers = boundedWorkers });
});

app.MapPost("/demo/dependency-failure", (
    int? seconds,
    DemoScenarioCoordinator coordinator,
    DemoWorkloads workloads) =>
{
    var boundedSeconds = DemoScenarioLimits.DependencyFailureSeconds(seconds ?? 20);
    var scenario = coordinator.Start(
        "dependency-failure",
        TimeSpan.FromSeconds(boundedSeconds + 2),
        cancellationToken => workloads.DependencyFailuresAsync(boundedSeconds, cancellationToken));
    return Results.Accepted("/demo/status", new { scenario, seconds = boundedSeconds });
});

app.MapGet("/demo/status", (DemoScenarioCoordinator coordinator) => Results.Ok(coordinator.Status()));
app.MapPost("/demo/reset", async (DemoScenarioCoordinator coordinator, CancellationToken cancellationToken) =>
    Results.Ok(await coordinator.ResetAsync(cancellationToken)));

app.Run();

public partial class Program;
