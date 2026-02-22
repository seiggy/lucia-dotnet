using lucia.Agents.Orchestration;
using lucia.Agents.Orchestration.Models;
using lucia.Agents.Training.Models;

namespace lucia.Tests.Orchestration;

/// <summary>
/// Test implementation of <see cref="IOrchestratorObserver"/> that captures
/// intermediate pipeline events for evaluation assertions.
/// </summary>
public sealed class OrchestratorEvalObserver : IOrchestratorObserver
{
    /// <summary>
    /// The routing decision made by the <see cref="RouterExecutor"/>,
    /// including agent ID, confidence, reasoning, and additional agents.
    /// </summary>
    public AgentChoiceResult? RoutingDecision { get; private set; }

    /// <summary>
    /// Per-agent execution results collected as each agent completes.
    /// </summary>
    public List<OrchestratorAgentResponse> AgentResponses { get; } = [];

    /// <summary>
    /// The final aggregated response composed by the <see cref="ResultAggregatorExecutor"/>.
    /// </summary>
    public string? AggregatedResponse { get; private set; }

    public string? UserRequest { get; private set; }

    public Task OnRequestStartedAsync(
        string userRequest,
        IReadOnlyList<TracedMessage>? conversationHistory = null,
        CancellationToken cancellationToken = default)
    {
        UserRequest = userRequest;
        return Task.CompletedTask;
    }

    public Task OnRoutingCompletedAsync(
        AgentChoiceResult result,
        string? systemPrompt = null,
        CancellationToken cancellationToken = default)
    {
        RoutingDecision = result;
        return Task.CompletedTask;
    }

    public Task OnAgentExecutionCompletedAsync(OrchestratorAgentResponse response, CancellationToken cancellationToken = default)
    {
        AgentResponses.Add(response);
        return Task.CompletedTask;
    }

    public Task OnResponseAggregatedAsync(string aggregatedResponse, CancellationToken cancellationToken = default)
    {
        AggregatedResponse = aggregatedResponse;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Resets all captured state for reuse across test iterations.
    /// </summary>
    public void Reset()
    {
        UserRequest = null;
        RoutingDecision = null;
        AgentResponses.Clear();
        AggregatedResponse = null;
    }
}
