using lucia.EvalHarness.Evaluation;

namespace lucia.EvalHarness.Tui;

/// <summary>
/// Aggregated scores and performance for a single base model evaluated on a single
/// inference backend across all agents. Used by <see cref="BackendComparisonRenderer"/>.
/// </summary>
internal sealed class BackendAggregation
{
    public required string BackendName { get; init; }
    public double AvgOverall { get; init; }
    public int TotalPassed { get; init; }
    public int TotalTests { get; init; }
    public required ModelPerformanceSummary Performance { get; init; }
    public double PassRate => TotalTests > 0 ? (double)TotalPassed / TotalTests : 0;
}
