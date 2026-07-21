namespace Tracebag.Demo.Api.Scenarios;

public static class DemoScenarioLimits
{
    public const int MaxActiveScenarios = 4;

    public static int CpuSeconds(int value) => RequireRange("seconds", value, 1, 30);
    public static int CpuWorkers(int value) => RequireRange("workers", value, 1, 4);
    public static int AllocationSeconds(int value) => RequireRange("seconds", value, 1, 30);
    public static int AllocationMegabytesPerSecond(int value) => RequireRange("mbPerSecond", value, 1, 32);
    public static int ExceptionCount(int value) => RequireRange("count", value, 1, 100);
    public static int SlowMilliseconds(int value) => RequireRange("milliseconds", value, 100, 5_000);
    public static int ContentionSeconds(int value) => RequireRange("seconds", value, 1, 30);
    public static int ContentionWorkers(int value) => RequireRange("workers", value, 2, 32);
    public static int StarvationSeconds(int value) => RequireRange("seconds", value, 1, 20);
    public static int StarvationWorkers(int value) => RequireRange("workers", value, 2, 32);
    public static int DependencyFailureSeconds(int value) => RequireRange("seconds", value, 1, 30);

    public static object Describe()
    {
        return new
        {
            maxActiveScenarios = MaxActiveScenarios,
            cpu = new { seconds = "1-30", workers = "1-4" },
            allocations = new { seconds = "1-30", mbPerSecond = "1-32" },
            exceptions = new { count = "1-100" },
            slow = new { milliseconds = "100-5000" },
            lockContention = new { seconds = "1-30", workers = "2-32" },
            threadpoolStarvation = new { seconds = "1-20", workers = "2-32" },
            dependencyFailure = new { seconds = "1-30" }
        };
    }

    private static int RequireRange(string parameter, int value, int minimum, int maximum)
    {
        if (value < minimum || value > maximum)
        {
            throw new DemoScenarioValidationException(
                parameter,
                $"{parameter} must be between {minimum} and {maximum}.");
        }

        return value;
    }
}

public sealed class DemoScenarioValidationException(string parameter, string message) : Exception(message)
{
    public string Parameter { get; } = parameter;
}
