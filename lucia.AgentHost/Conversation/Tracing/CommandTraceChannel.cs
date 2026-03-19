using System.Threading.Channels;

using lucia.Agents.CommandTracing;

namespace lucia.AgentHost.Conversation.Tracing;

/// <summary>
/// In-memory bounded channel that pushes new command traces to SSE consumers.
/// The <see cref="ConversationCommandProcessor"/> writes; the SSE endpoint reads.
/// </summary>
public sealed class CommandTraceChannel
{
    private readonly Channel<CommandTrace> _channel = Channel.CreateBounded<CommandTrace>(
        new BoundedChannelOptions(50)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleWriter = false,
            SingleReader = false,
        });

    public void Write(CommandTrace trace) => _channel.Writer.TryWrite(trace);

    public IAsyncEnumerable<CommandTrace> ReadAllAsync(CancellationToken ct)
        => _channel.Reader.ReadAllAsync(ct);
}
