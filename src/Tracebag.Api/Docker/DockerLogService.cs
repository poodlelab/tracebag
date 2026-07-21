using System.Text;
using System.Globalization;
using Docker.DotNet;
using Docker.DotNet.Models;
using Tracebag.Api.Logs;
using Tracebag.Api.Models;

namespace Tracebag.Api.Docker;

public sealed class DockerLogService
{
    private readonly DockerClientFactory _dockerClientFactory;
    private readonly ContainerCatalog _containerCatalog;

    public DockerLogService(DockerClientFactory dockerClientFactory, ContainerCatalog containerCatalog)
    {
        _dockerClientFactory = dockerClientFactory;
        _containerCatalog = containerCatalog;
    }

    public async Task<IReadOnlyList<LogEventDto>> GetTailAsync(string containerId, int tail, bool timestamps, CancellationToken cancellationToken)
    {
        var container = await _containerCatalog.GetAllowedAsync(containerId, cancellationToken);
        var stream = await OpenLogStreamAsync(container.ID, follow: false, tail, timestamps, cancellationToken);
        var events = new List<LogEventDto>();
        await foreach (var logEvent in ReadEventsAsync(stream, cancellationToken))
        {
            events.Add(logEvent);
        }

        return events;
    }

    public async IAsyncEnumerable<LogEventDto> StreamAsync(
        string containerId,
        int tail,
        bool timestamps,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var container = await _containerCatalog.GetAllowedAsync(containerId, cancellationToken);
        var stream = await OpenLogStreamAsync(container.ID, follow: true, tail, timestamps, cancellationToken);
        await foreach (var logEvent in ReadEventsAsync(stream, cancellationToken))
        {
            yield return logEvent;
        }
    }

    public async Task<string> CollectRunnerLogsAsync(string containerId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        var stream = await OpenLogStreamAsync(containerId, follow: false, tail: 1000, timestamps: false, timeoutCts.Token);
        var builder = new StringBuilder();
        await foreach (var logEvent in ReadEventsAsync(stream, timeoutCts.Token))
        {
            builder.AppendLine(logEvent.Line);
        }

        return builder.ToString();
    }

    public async IAsyncEnumerable<LogEventDto> StreamRunnerLogsAsync(
        string containerId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var stream = await OpenLogStreamAsync(containerId, follow: true, tail: 100, timestamps: false, cancellationToken);
        await foreach (var logEvent in ReadEventsAsync(stream, cancellationToken))
        {
            yield return logEvent;
        }
    }

    private async Task<MultiplexedStream> OpenLogStreamAsync(
        string containerId,
        bool follow,
        int tail,
        bool timestamps,
        CancellationToken cancellationToken)
    {
        return await _dockerClientFactory.Client.Containers.GetContainerLogsAsync(
            containerId,
            tty: false,
            new ContainerLogsParameters
            {
                ShowStdout = true,
                ShowStderr = true,
                Follow = follow,
                Timestamps = timestamps,
                Tail = Math.Clamp(tail, 1, 1000).ToString(CultureInfo.InvariantCulture)
            },
            cancellationToken);
    }

    public async Task<MultiplexedStream> OpenIngestionStreamAsync(
        string dockerId,
        DateTimeOffset? since,
        CancellationToken cancellationToken)
    {
        return await _dockerClientFactory.Client.Containers.GetContainerLogsAsync(
            dockerId,
            tty: false,
            new ContainerLogsParameters
            {
                ShowStdout = true,
                ShowStderr = true,
                Follow = true,
                Timestamps = true,
                Since = since?.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture) ?? "0",
                Tail = "all"
            },
            cancellationToken);
    }

    public static IAsyncEnumerable<LogEventDto> ReadEventsAsync(
        MultiplexedStream stream,
        CancellationToken cancellationToken)
    {
        return ReadEventsAsync(stream, 262_144, cancellationToken);
    }

    public static async IAsyncEnumerable<LogEventDto> ReadEventsAsync(
        MultiplexedStream stream,
        int maximumLineBytes,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using (stream)
        {
            var buffer = new byte[16 * 1024];
            var decoder = new DockerLogLineDecoder(maximumLineBytes);

            while (!cancellationToken.IsCancellationRequested)
            {
                var result = await stream.ReadOutputAsync(buffer, 0, buffer.Length, cancellationToken);
                if (result.EOF || result.Count == 0)
                {
                    break;
                }

                var streamName = result.Target == MultiplexedStream.TargetStream.StandardError ? "stderr" : "stdout";
                foreach (var line in decoder.Append(streamName, buffer.AsSpan(0, result.Count)))
                {
                    yield return new LogEventDto(line.Stream, line.Text, TryReadTimestamp(line.Text));
                }
            }

            foreach (var line in decoder.Complete())
            {
                yield return new LogEventDto(line.Stream, line.Text, TryReadTimestamp(line.Text));
            }
        }
    }

    private static DateTimeOffset? TryReadTimestamp(string line)
    {
        var firstSpace = line.IndexOf(' ');
        if (firstSpace <= 0)
        {
            return null;
        }

        var candidate = line[..firstSpace];
        return DateTimeOffset.TryParse(candidate, out var timestamp) ? timestamp : null;
    }
}
