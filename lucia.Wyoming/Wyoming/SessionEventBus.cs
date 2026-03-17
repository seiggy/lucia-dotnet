using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace lucia.Wyoming.Wyoming;

/// <summary>
/// Broadcast event bus for Wyoming session events.
/// Each call to <see cref="SubscribeAsync"/> creates an independent subscription
/// that receives all events published after subscription starts.
/// </summary>
public sealed class SessionEventBus
{
    private readonly object _lock = new();
    private readonly List<Channel<SessionEvent>> _subscribers = [];

    public void Publish(SessionEvent evt)
    {
        lock (_lock)
        {
            foreach (var subscriber in _subscribers)
            {
                subscriber.Writer.TryWrite(evt);
            }
        }
    }

    public async IAsyncEnumerable<SessionEvent> SubscribeAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var channel = Channel.CreateBounded<SessionEvent>(
            new BoundedChannelOptions(256)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleWriter = false,
                SingleReader = true,
            });

        lock (_lock)
        {
            _subscribers.Add(channel);
        }

        try
        {
            await foreach (var evt in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                yield return evt;
            }
        }
        finally
        {
            lock (_lock)
            {
                _subscribers.Remove(channel);
            }

            channel.Writer.TryComplete();
        }
    }
}
