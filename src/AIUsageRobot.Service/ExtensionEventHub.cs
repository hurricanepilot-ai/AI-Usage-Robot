using System.Collections.Concurrent;
using System.Threading.Channels;

namespace AIUsageRobot.Service;

public sealed class ExtensionEventHub
{
    private readonly ConcurrentDictionary<Guid, Channel<string>> _subscribers = new();

    public (Guid Id, ChannelReader<string> Reader) Subscribe()
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(8)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
        _subscribers[id] = channel;
        return (id, channel.Reader);
    }

    public void Unsubscribe(Guid id)
    {
        if (_subscribers.TryRemove(id, out var channel)) channel.Writer.TryComplete();
    }

    public int Publish(string eventName)
    {
        var delivered = 0;
        foreach (var subscriber in _subscribers.Values)
            if (subscriber.Writer.TryWrite(eventName)) delivered++;
        return delivered;
    }
}
