namespace lucia.Agents.Orchestration.Models;

/// <summary>
/// Represents a failure surfaced during aggregation.
/// </summary>
public sealed record AggregatedFailure(string AgentId, string Error);
