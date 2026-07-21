using System.Collections.Concurrent;
using System.Threading.Channels;
using Tracebag.Api.Models;

namespace Tracebag.Api.Logs;

public sealed class LogLiveHub
{
    private readonly ConcurrentDictionary<Guid, Subscription> _subscriptions = new();

    public LogSubscription Subscribe(string containerId)
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<LogSearchEntryDto>(new BoundedChannelOptions(1_000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
        _subscriptions[id] = new Subscription(containerId, channel);
        return new LogSubscription(channel.Reader, () => _subscriptions.TryRemove(id, out _));
    }

    public void Publish(IEnumerable<LogSearchEntryDto> entries)
    {
        foreach (var entry in entries)
        {
            foreach (var subscription in _subscriptions.Values)
            {
                if (string.Equals(subscription.ContainerId, entry.ContainerId, StringComparison.Ordinal))
                {
                    subscription.Channel.Writer.TryWrite(entry);
                }
            }
        }
    }

    private sealed record Subscription(string ContainerId, Channel<LogSearchEntryDto> Channel);
}

public sealed class LogSubscription(ChannelReader<LogSearchEntryDto> reader, Action dispose) : IDisposable
{
    public ChannelReader<LogSearchEntryDto> Reader { get; } = reader;

    public void Dispose()
    {
        dispose();
    }
}
