using lucia.EvalHarness.Configuration;

namespace lucia.EvalHarness.Tui;

/// <summary>
/// Aggregated scores for a single model evaluated with a single parameter profile
/// across all agents. Used by <see cref="ProfileComparisonRenderer"/> for comparison reporting.
/// </summary>
internal sealed class ProfileAggregation
{
    public required string ProfileName { get; init; }
    public required ModelParameterProfile Profile { get; init; }
    public double AvgOverall { get; init; }
    public double AvgToolSelection { get; init; }
    public double AvgToolSuccess { get; init; }
    public double AvgToolEfficiency { get; init; }
    public double AvgTaskCompletion { get; init; }
    public int TotalPassed { get; init; }
    public int TotalTests { get; init; }
    public double AvgLatencyMs { get; init; }
    public double PassRate => TotalTests > 0 ? (double)TotalPassed / TotalTests : 0;
}
