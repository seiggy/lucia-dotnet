using lucia.AgentHost.Conversation;

namespace lucia.AgentHost.Models;

public sealed class ActivitySummary
{
    public required object Traces { get; init; }
    public required object Tasks { get; init; }
    public required object Cache { get; init; }
    public required object ChatCache { get; init; }
    public required ConversationStats Conversation { get; init; }
}
