namespace lucia.EvalHarness.Evaluation;

/// <summary>Quality is primary. Known mean provider cost breaks exact quality ties, then the model name.</summary>
public sealed record ModelRecommendation
{
    public required string ModelName { get; init; }
    public required double AverageScore { get; init; }
    public double? MeanLatencyMs { get; init; }
    public int PassedCount { get; init; }
    public int ScoredTestCount { get; init; }
    public decimal? MeanTestCostUsd { get; init; }
    public required InferenceCostSummary Cost { get; init; }

    public static IReadOnlyList<ModelRecommendation> Rank(EvalRunResult result) =>
        result.AgentResults.SelectMany(agent => agent.ModelResults)
            .GroupBy(model => model.ModelName)
            .Where(group => group.Any(model => model.OverallScore.HasValue))
            .Select(group =>
            {
                var tests = group.SelectMany(model => model.TestCaseResults).ToList();
                var cost = InferenceCostSummary.Aggregate(tests.Select(test => test.Cost));
                return new ModelRecommendation
                {
                    ModelName = group.Key,
                    AverageScore = group.Select(model => model.OverallScore).OfType<double>().Average(),
                    MeanLatencyMs = group.Where(model => model.Performance.RunCount > 0)
                        .Select(model => (double?)model.Performance.MeanLatency.TotalMilliseconds).Average(),
                    PassedCount = group.Sum(model => model.PassedCount),
                    ScoredTestCount = group.Sum(model => model.ScoredTestCaseCount),
                    Cost = cost,
                    MeanTestCostUsd = tests.Count > 0 ? cost.EstimatedUsd / tests.Count : null
                };
            })
            .OrderByDescending(model => model.AverageScore)
            .ThenBy(model => model.MeanTestCostUsd ?? decimal.MaxValue)
            .ThenBy(model => model.ModelName, StringComparer.Ordinal)
            .ToList();
}
