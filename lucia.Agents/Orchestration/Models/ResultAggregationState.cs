namespace lucia.Agents.Orchestration.Models;

/// <summary>
/// State persisted between aggregator invocations.
/// </summary>
public sealed class ResultAggregationState
{
    /// <summary>
    /// Agent responses collected so far.
    /// </summary>
    public Dictionary<string, OrchestratorAgentResponse> Responses { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
