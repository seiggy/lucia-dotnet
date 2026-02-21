using Microsoft.Agents.AI;

namespace lucia.Agents.Orchestration;

/// <summary>
/// Provides <see cref="AIAgent"/> instances to the orchestrator. When registered,
/// the orchestrator uses these agents instead of converting <c>AgentCard</c> instances
/// via the framework's <c>AsAIAgent()</c> extension. This allows evaluation tests to
/// supply real or stubbed agent implementations.
/// </summary>
public interface IAgentProvider
{
    /// <summary>
    /// Returns the set of <see cref="AIAgent"/> instances available for orchestration.
    /// </summary>
    Task<IReadOnlyList<AIAgent>> GetAgentsAsync(CancellationToken cancellationToken = default);
}
