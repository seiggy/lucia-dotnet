using lucia.Agents.Orchestration.Models;
using lucia.Agents.Training.Models;

namespace lucia.Agents.Orchestration;

/// <summary>
/// Observer interface for capturing intermediate orchestration pipeline events.
/// Implementations receive callbacks at each stage of the Router → Dispatch → Aggregator
/// workflow, enabling evaluation tests to inspect routing decisions, per-agent responses,
/// and aggregated results without changing the production return type.
/// </summary>
public interface IOrchestratorObserver
{
    /// <summary>
    /// Called before the workflow begins to initialize per-request tracking state.
    /// Must be called in the parent async context so that AsyncLocal state flows
    /// correctly into the workflow child contexts.
    /// </summary>
    /// <param name="userRequest">The original user request text.</param>
    /// <param name="conversationHistory">Prior conversation turns for multi-turn context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task OnRequestStartedAsync(
        string userRequest,
        IReadOnlyList<TracedMessage>? conversationHistory = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Called after the router has selected an agent (or agents) for the request.
    /// </summary>
    /// <param name="result">The routing decision including agent ID, confidence, reasoning, and additional agents.</param>
    /// <param name="systemPrompt">The system prompt used by the router for this decision.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task OnRoutingCompletedAsync(
        AgentChoiceResult result,
        string? systemPrompt = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Called after each individual agent has completed (or failed) execution.
    /// May be invoked multiple times if the router selected additional agents.
    /// </summary>
    /// <param name="response">The agent's execution result including content, success status, and timing.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task OnAgentExecutionCompletedAsync(OrchestratorAgentResponse response, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called after the result aggregator has composed the final response from all agent outputs.
    /// </summary>
    /// <param name="aggregatedResponse">The final aggregated text response.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task OnResponseAggregatedAsync(string aggregatedResponse, CancellationToken cancellationToken = default);
}
