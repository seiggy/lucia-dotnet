namespace lucia.Agents.CommandTracing;

/// <summary>
/// Aggregate statistics for command traces.
/// </summary>
public sealed record CommandTraceStats
{
    public required long TotalCount { get; init; }
    public required long CommandHandledCount { get; init; }
    public required long LlmFallbackCount { get; init; }
    public required long ErrorCount { get; init; }
    public required double AvgDurationMs { get; init; }
    public required IReadOnlyDictionary<string, long> BySkill { get; init; }
}
