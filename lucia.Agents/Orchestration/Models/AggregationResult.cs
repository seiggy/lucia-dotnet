namespace lucia.Agents.Orchestration.Models;

/// <summary>
/// Aggregation telemetry emitted to workflow context subscribers.
/// </summary>
public sealed record AggregationResult(
    string Message,
    IReadOnlyList<string> SuccessfulAgents,
    IReadOnlyList<AggregatedFailure> FailedAgents,
    long TotalExecutionTimeMs);
