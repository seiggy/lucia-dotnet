namespace lucia.Agents.Orchestration;

/// <summary>
/// State persisted between aggregator invocations.
/// </summary>
public sealed class ResultAggregationState
{
    /// <summary>
    /// Agent responses collected so far.
    /// </summary>
    public Dictionary<string, AgentResponse> Responses { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
