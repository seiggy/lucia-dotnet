namespace lucia.EvalHarness.Evaluation;

/// <summary>
/// Pure aggregation helpers for multi-run parameter sweep results.
/// Each combination is evaluated N times; these methods compute the statistics
/// that drive winner selection and report generation.
/// </summary>
public static class SweepRunAggregator
{
    /// <summary>
    /// Computes the overall mean score across all runs and all agents.
    /// </summary>
    public static double ComputeMean(IReadOnlyList<IReadOnlyList<ModelEvalResult>> allRunResults)
    {
        var all = allRunResults.SelectMany(run => run).ToList();
        return all.Count > 0 ? all.Average(r => r.OverallScore) : 0.0;
    }

    /// <summary>
    /// Computes the variance of per-run mean scores.
    /// Low variance indicates the combination is stable; high variance means
    /// the result is noisy and the mean is less trustworthy.
    /// </summary>
    public static double ComputeVariance(IReadOnlyList<IReadOnlyList<ModelEvalResult>> allRunResults)
    {
        if (allRunResults.Count < 2)
            return 0.0;

        var runMeans = allRunResults
            .Select(run => run.Count > 0 ? run.Average(r => r.OverallScore) : 0.0)
            .ToList();

        var mean = runMeans.Average();
        return runMeans.Average(m => (m - mean) * (m - mean));
    }

    /// <summary>
    /// Returns the lowest per-run mean score — the pessimistic bound.
    /// </summary>
    public static double ComputeMinRunMean(IReadOnlyList<IReadOnlyList<ModelEvalResult>> allRunResults)
    {
        if (allRunResults.Count == 0)
            return 0.0;

        return allRunResults
            .Select(run => run.Count > 0 ? run.Average(r => r.OverallScore) : 0.0)
            .Min();
    }

    /// <summary>
    /// Selects the winning entry from a list of sweep entries.
    /// Primary criterion: highest mean score across N runs.
    /// Tie-breaker: lower score variance (more stable combination wins).
    /// </summary>
    public static SweepEntry SelectWinner(IReadOnlyList<SweepEntry> entries) =>
        entries
            .OrderByDescending(e => e.MeanScore)
            .ThenBy(e => e.ScoreVariance)
            .First();

    /// <summary>
    /// Derives a per-run seed from a base seed so each run is reproducible
    /// yet distinct. Returns <see langword="null"/> when no base seed is set.
    /// </summary>
    public static int? DeriveRunSeed(int? baseSeed, int runIndex) =>
        baseSeed.HasValue ? baseSeed.Value + runIndex : null;
}
