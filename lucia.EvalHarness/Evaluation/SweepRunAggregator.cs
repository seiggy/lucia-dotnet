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
    public static double? ComputeMean(IReadOnlyList<IReadOnlyList<ModelEvalResult>> allRunResults)
    {
        var available = allRunResults
            .SelectMany(run => run)
            .Select(result => result.OverallScore)
            .OfType<double>()
            .ToList();
        return available.Count > 0 ? available.Average() : null;
    }

    /// <summary>
    /// Computes the variance of per-run mean scores.
    /// Low variance indicates the combination is stable; high variance means
    /// the result is noisy and the mean is less trustworthy.
    /// </summary>
    public static double? ComputeVariance(IReadOnlyList<IReadOnlyList<ModelEvalResult>> allRunResults)
    {
        var runMeans = allRunResults
            .Select(run => run.Select(result => result.OverallScore).OfType<double>().ToList())
            .Where(scores => scores.Count > 0)
            .Select(scores => scores.Average())
            .ToList();
        if (runMeans.Count == 0)
            return null;
        if (runMeans.Count == 1)
            return 0.0;

        var mean = runMeans.Average();
        return runMeans.Average(m => (m - mean) * (m - mean));
    }

    /// <summary>
    /// Returns the lowest per-run mean score — the pessimistic bound.
    /// </summary>
    public static double? ComputeMinRunMean(IReadOnlyList<IReadOnlyList<ModelEvalResult>> allRunResults)
    {
        var runMeans = allRunResults
            .Select(run => run.Select(result => result.OverallScore).OfType<double>().ToList())
            .Where(scores => scores.Count > 0)
            .Select(scores => scores.Average())
            .ToList();
        return runMeans.Count > 0 ? runMeans.Min() : null;
    }

    /// <summary>
    /// Selects the winning entry from a list of sweep entries.
    /// Primary criterion: highest mean score across N runs.
    /// Tie-breaker: lower score variance (more stable combination wins).
    /// </summary>
    public static SweepEntry? SelectWinner(IReadOnlyList<SweepEntry> entries) =>
        entries
            .Where(entry => entry.MeanScore.HasValue)
            .OrderByDescending(e => e.MeanScore)
            .ThenBy(e => e.ScoreVariance ?? double.MaxValue)
            .FirstOrDefault();

    /// <summary>
    /// Derives a per-run seed from a base seed so each run is reproducible
    /// yet distinct. Returns <see langword="null"/> when no base seed is set.
    /// </summary>
    public static int? DeriveRunSeed(int? baseSeed, int runIndex) =>
        baseSeed.HasValue ? baseSeed.Value + runIndex : null;
}
