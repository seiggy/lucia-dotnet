namespace lucia.EvalHarness.Evaluation;

/// <summary>
/// Point-in-time performance data for a single evaluation run.
/// </summary>
public sealed class PerformanceSnapshot
{
    /// <summary>End-to-end evaluation duration including all tool calls.</summary>
    public TimeSpan TotalDuration { get; init; }

    /// <summary>When this measurement was taken.</summary>
    public DateTimeOffset Timestamp { get; init; }
}
