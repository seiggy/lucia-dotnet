namespace lucia.EvalHarness.Personality;

/// <summary>
/// Aggregated report for a single model's personality eval run using LLM-as-Judge scoring.
/// Contains all (scenario × profile) results and computed summaries.
/// </summary>
public sealed class PersonalityEvalReport
{
    public required string ModelName { get; init; }
    public required string JudgeModelName { get; init; }
    public required IReadOnlyList<PersonalityScenarioResult> Results { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public required DateTimeOffset CompletedAt { get; init; }

    public int TotalCombinations => Results.Count;

    /// <summary>
    /// Average combined score across all successfully judged results (1-5 scale).
    /// Timed-out judge results are excluded so a judge outage doesn't lower scores.
    /// </summary>
    public double? AverageCombinedScore => Average(
        Results.Where(result => !result.TimedOut).Select(result => result.JudgeResult?.CombinedScore));

    public IReadOnlyList<string> ScenarioIds =>
        Results.Select(r => r.ScenarioId).Distinct().ToList();

    public IReadOnlyList<string> ProfileIds =>
        Results.Select(r => r.ProfileId).Distinct().ToList();

    /// <summary>
    /// Average personality adherence score across all successfully judged results (0-5).
    /// Timed-out judge results are excluded.
    /// </summary>
    public double? AveragePersonalityScore => Average(
        Results.Where(result => !result.TimedOut).Select(result => result.JudgeResult?.PersonalityScore is { } score
            ? (double?)score
            : null));

    /// <summary>
    /// Average meaning preservation score across all successfully judged results (0-5).
    /// Timed-out judge results are excluded.
    /// </summary>
    public double? AverageMeaningScore => Average(
        Results.Where(result => !result.TimedOut).Select(result => result.JudgeResult?.MeaningScore is { } score
            ? (double?)score
            : null));

    /// <summary>
    /// Results where meaning score is below 3 — the dangerous failures.
    /// Timed-out judge results are excluded so a judge outage isn't reported as meaning loss.
    /// </summary>
    public IReadOnlyList<PersonalityScenarioResult> MeaningFailures =>
        Results.Where(result => !result.TimedOut && result.JudgeResult?.MeaningScore is < 3).ToList();

    /// <summary>
    /// Results that failed because the model-under-test or judge call exceeded its deadline.
    /// </summary>
    public IReadOnlyList<PersonalityScenarioResult> Timeouts =>
        Results.Where(result => result.TimedOut).ToList();

    private static double? Average(IEnumerable<double?> scores)
    {
        var available = scores.OfType<double>().ToList();
        return available.Count > 0 ? available.Average() : null;
    }
}
