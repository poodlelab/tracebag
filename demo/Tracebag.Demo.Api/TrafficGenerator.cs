namespace Tracebag.Demo.Api;

public static class TrafficGenerator
{
    public static async Task RunAsync(string baseUrl, CancellationToken cancellationToken)
    {
        using var client = new HttpClient { BaseAddress = new Uri(baseUrl) };
        var interval = ParseInterval(Environment.GetEnvironmentVariable("TRACEBAG_DEMO_TRAFFIC_INTERVAL_MS"));
        Console.WriteLine($"Tracebag demo traffic generator targeting {baseUrl} every {interval.TotalMilliseconds} ms.");

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var response = await client.GetAsync("/demo/healthy", cancellationToken);
                Console.WriteLine($"Demo traffic response {(int)response.StatusCode} at {DateTimeOffset.UtcNow:O}.");
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                Console.Error.WriteLine($"Demo traffic request failed: {exception.Message}");
            }

            await Task.Delay(interval, cancellationToken);
        }
    }

    private static TimeSpan ParseInterval(string? raw)
    {
        return int.TryParse(raw, out var milliseconds)
            ? TimeSpan.FromMilliseconds(Math.Clamp(milliseconds, 250, 10_000))
            : TimeSpan.FromSeconds(1);
    }
}
