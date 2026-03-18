namespace lucia.AgentHost.Conversation;

/// <summary>
/// Snapshot of conversation routing statistics for the activity summary.
/// </summary>
public sealed record ConversationStats
{
    /// <summary>Commands handled by the pattern parser (no LLM).</summary>
    public long CommandParsed { get; init; }

    /// <summary>Requests forwarded to the LLM orchestrator.</summary>
    public long LlmFallback { get; init; }

    /// <summary>Command parser execution failures.</summary>
    public long Errors { get; init; }

    /// <summary>Total conversation requests (parsed + LLM).</summary>
    public long Total { get; init; }

    /// <summary>Ratio of commands handled by parser vs total (0.0–1.0).</summary>
    public double CommandRate { get; init; }
}
