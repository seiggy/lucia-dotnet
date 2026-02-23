using System.Threading.Channels;
using lucia.Agents.Orchestration.Models;

namespace lucia.Agents.Orchestration;

/// <summary>
/// In-memory bounded channel that bridges the <see cref="IOrchestratorObserver"/>
/// pipeline events to the SSE endpoint for the live activity dashboard.
/// Registered as a singleton; writers are observers, readers are SSE connections.
/// Uses DropOldest to prevent backpressure from slow/disconnected consumers.
/// </summary>
public sealed class LiveActivityChannel
{
    private readonly Channel<LiveEvent> _channel = Channel.CreateBounded<LiveEvent>(
        new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleWriter = false,
            SingleReader = false,
        });

    /// <summary>
    /// Publish an event to all SSE consumers. Non-blocking; drops oldest if full.
    /// </summary>
    public void Write(LiveEvent evt)
    {
        _channel.Writer.TryWrite(evt);
    }

    /// <summary>
    /// Async publish for callers that need backpressure awareness.
    /// </summary>
    public ValueTask WriteAsync(LiveEvent evt, CancellationToken cancellationToken = default)
    {
        return _channel.Writer.TryWrite(evt)
            ? default
            : _channel.Writer.WriteAsync(evt, cancellationToken);
    }

    /// <summary>
    /// Read all events as an async stream. Each SSE connection calls this independently.
    /// </summary>
    public IAsyncEnumerable<LiveEvent> ReadAllAsync(CancellationToken cancellationToken)
    {
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }
}
