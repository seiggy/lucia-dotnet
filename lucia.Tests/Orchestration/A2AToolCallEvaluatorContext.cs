#pragma warning disable AIEVAL001 // Microsoft.Extensions.AI.Evaluation is experimental

using lucia.Agents.Orchestration.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;

namespace lucia.Tests.Orchestration;

/// <summary>
/// Provides captured orchestrator pipeline data to the <see cref="A2AToolCallEvaluator"/>.
/// Wraps the <see cref="OrchestratorEvalObserver"/> snapshot taken after a full
/// Router → AgentDispatch → ResultAggregator run.
/// </summary>
public sealed class A2AToolCallEvaluatorContext : EvaluationContext
{
    /// <summary>
    /// The routing decision made by the <see cref="lucia.Agents.Orchestration.RouterExecutor"/>.
    /// </summary>
    public AgentChoiceResult? RoutingDecision { get; }

    /// <summary>
    /// Per-agent execution results collected by the observer.
    /// </summary>
    public IReadOnlyList<OrchestratorAgentResponse> AgentResponses { get; }

    /// <summary>
    /// The final aggregated response from the <see cref="lucia.Agents.Orchestration.ResultAggregatorExecutor"/>.
    /// </summary>
    public string? AggregatedResponse { get; }

    /// <summary>
    /// Agent IDs that the test expects the orchestrator to route to.
    /// Used by <see cref="A2AToolCallEvaluator"/> to verify routing accuracy.
    /// </summary>
    public IReadOnlySet<string> ExpectedAgentIds { get; }

    public A2AToolCallEvaluatorContext(
        OrchestratorEvalObserver observer,
        IEnumerable<string> expectedAgentIds)
        : base("A2AToolCallContext", BuildContents(observer))
    {
        RoutingDecision = observer.RoutingDecision;
        AgentResponses = observer.AgentResponses.ToList().AsReadOnly();
        AggregatedResponse = observer.AggregatedResponse;
        ExpectedAgentIds = new HashSet<string>(expectedAgentIds, StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<AIContent> BuildContents(OrchestratorEvalObserver observer)
    {
        yield return new TextContent(
            $"Routing: {observer.RoutingDecision?.AgentId ?? "(none)"}, " +
            $"Confidence: {observer.RoutingDecision?.Confidence:F2}, " +
            $"Agents invoked: {observer.AgentResponses.Count}, " +
            $"Aggregated: {(observer.AggregatedResponse is not null ? "yes" : "no")}");
    }
}
