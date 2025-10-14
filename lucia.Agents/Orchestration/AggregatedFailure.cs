namespace lucia.Agents.Orchestration;

/// <summary>
/// Represents a failure surfaced during aggregation.
/// </summary>
public sealed record AggregatedFailure(string AgentId, string Error);
