using lucia.Agents.Orchestration.Models;
using Microsoft.Extensions.AI;

namespace lucia.Agents.Orchestration;

/// <summary>
/// Invokes a single agent (local or remote) and returns a structured response.
/// </summary>
public interface IAgentInvoker
{
    /// <summary>
    /// Identifier of the agent this invoker targets.
    /// </summary>
    string AgentId { get; }

    /// <summary>
    /// Invoke the agent with the given message and return its response.
    /// </summary>
    ValueTask<OrchestratorAgentResponse> InvokeAsync(
        ChatMessage message,
        CancellationToken cancellationToken);
}
