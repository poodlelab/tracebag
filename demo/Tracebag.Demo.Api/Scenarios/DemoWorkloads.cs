using System.Diagnostics;

namespace Tracebag.Demo.Api.Scenarios;

public sealed class DemoWorkloads(ILogger<DemoWorkloads> logger)
{
    private readonly object _contentionGate = new();

    public async Task CpuAsync(int seconds, int workers, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Creating bounded CPU pressure for {Seconds} seconds with {Workers} workers.",
            seconds,
            workers);
        var deadline = Stopwatch.GetTimestamp() + (long)(seconds * Stopwatch.Frequency);
        var tasks = Enumerable.Range(0, workers).Select(worker => Task.Run(() =>
        {
            var value = worker + 0.5;
            while (Stopwatch.GetTimestamp() < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                value = Math.Sqrt(value * value + 1.23456789);
            }

            GC.KeepAlive(value);
        }, cancellationToken));
        await Task.WhenAll(tasks);
    }

    public async Task AllocationsAsync(int seconds, int megabytesPerSecond, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Creating bounded allocation pressure for {Seconds} seconds at {MegabytesPerSecond} MB/s.",
            seconds,
            megabytesPerSecond);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(seconds);
        var bytesPerTick = megabytesPerSecond * 1024 * 1024 / 4;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = bytesPerTick;
            var allocations = new List<byte[]>();
            while (remaining > 0)
            {
                var length = Math.Min(remaining, 1024 * 1024);
                var allocation = new byte[length];
                allocation[0] = 1;
                allocation[^1] = 1;
                allocations.Add(allocation);
                remaining -= length;
            }

            GC.KeepAlive(allocations);
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        }
    }

    public async Task ExceptionsAsync(int count, CancellationToken cancellationToken)
    {
        for (var index = 1; index <= count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                throw new InvalidOperationException($"Synthetic checkout failure {index}.");
            }
            catch (InvalidOperationException exception)
            {
                logger.LogError(
                    exception,
                    "Demo exception {ExceptionIndex} of {ExceptionCount} for operation {Operation}.",
                    index,
                    count,
                    "checkout");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken);
        }
    }

    public async Task SlowRequestAsync(int milliseconds, CancellationToken cancellationToken)
    {
        logger.LogWarning("Starting simulated slow request lasting {Milliseconds} ms.", milliseconds);
        await Task.Delay(milliseconds, cancellationToken);
        logger.LogInformation("Simulated slow request completed after {Milliseconds} ms.", milliseconds);
    }

    public async Task LockContentionAsync(int seconds, int workers, CancellationToken cancellationToken)
    {
        logger.LogWarning(
            "Creating bounded lock contention for {Seconds} seconds with {Workers} workers.",
            seconds,
            workers);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(seconds);
        var tasks = Enumerable.Range(0, workers).Select(_ => Task.Run(() =>
        {
            while (DateTimeOffset.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                lock (_contentionGate)
                {
                    Thread.SpinWait(20_000);
                    Thread.Sleep(5);
                }
            }
        }, cancellationToken));
        await Task.WhenAll(tasks);
    }

    public async Task ThreadPoolStarvationAsync(int seconds, int workers, CancellationToken cancellationToken)
    {
        logger.LogWarning(
            "Creating bounded ThreadPool starvation for {Seconds} seconds with {Workers} blocking workers.",
            seconds,
            workers);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(seconds);
        var tasks = Enumerable.Range(0, workers).Select(_ => Task.Run(() =>
        {
            while (DateTimeOffset.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Thread.Sleep(200);
            }
        }, cancellationToken));
        await Task.WhenAll(tasks);
    }

    public async Task DependencyFailuresAsync(int seconds, CancellationToken cancellationToken)
    {
        logger.LogWarning("Simulating downstream timeouts for {Seconds} seconds.", seconds);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(seconds);
        var attempt = 0;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempt++;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(100));
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(
                    "Downstream request {Attempt} to {Dependency} timed out with status {StatusCode}.",
                    attempt,
                    "payments-demo",
                    504);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(150), cancellationToken);
        }
    }
}
