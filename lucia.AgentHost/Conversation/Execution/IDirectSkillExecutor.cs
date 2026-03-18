using lucia.AgentHost.Conversation.Models;
using lucia.Wyoming.CommandRouting;

namespace lucia.AgentHost.Conversation.Execution;

/// <summary>
/// Executes a matched command route by calling the target skill directly, bypassing LLM processing.
/// This is the fast-path executor for high-confidence command pattern matches.
/// </summary>
public interface IDirectSkillExecutor
{
    /// <summary>
    /// Invokes the skill method identified by <paramref name="route"/> and returns the execution outcome.
    /// </summary>
    Task<SkillExecutionResult> ExecuteAsync(
        CommandRouteResult route,
        ConversationContext context,
        CancellationToken ct = default);
}
