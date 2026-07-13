using lucia.EvalHarness.Configuration;

namespace lucia.EvalHarness.Evaluation;

public sealed class SweepEntry
{
    public required ModelParameterProfile Profile { get; init; }
    public required IReadOnlyList<ModelEvalResult> Results { get; init; }
    public required IReadOnlyList<IReadOnlyList<ModelEvalResult>> AllRunResults { get; init; }
    public double? MeanScore => SweepRunAggregator.ComputeMean(AllRunResults);
    public double? ScoreVariance => SweepRunAggregator.ComputeVariance(AllRunResults);
    public double? ScoreStdDev => ScoreVariance.HasValue ? Math.Sqrt(ScoreVariance.Value) : null;
    public double? MinRunMean => SweepRunAggregator.ComputeMinRunMean(AllRunResults);
    public double? AverageScore => MeanScore;

    public double? AverageLatencyMs
    {
        get
        {
            var all = AllRunResults
                .SelectMany(run => run)
                .Where(result => result.OverallScore.HasValue)
                .ToList();
            return all.Count > 0
                ? all.Average(result => result.Performance.MeanLatency.TotalMilliseconds)
                : null;
        }
    }
}
