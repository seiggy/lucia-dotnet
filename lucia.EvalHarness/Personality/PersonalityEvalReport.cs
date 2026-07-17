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
    /// Average combined score across all successfully judged results (1–5 scale).
    /// Timed-out judge results are excluded so a judge outage doesn't lower scores.
    /// </summary>
    public double AverageCombinedScore =>
        Results.Where(r => r.JudgeResult is { TimedOut: false }).Select(r => r.JudgeResult!.CombinedScore).DefaultIfEmpty(0).Average();

    public IReadOnlyList<string> ScenarioIds =>
        Results.Select(r => r.ScenarioId).Distinct().ToList();

    public IReadOnlyList<string> ProfileIds =>
        Results.Select(r => r.ProfileId).Distinct().ToList();

    /// <summary>
    /// Average personality adherence score across all successfully judged results (0-5).
    /// Timed-out judge results are excluded.
    /// </summary>
    public double AveragePersonalityScore =>
        Results.Where(r => r.JudgeResult is { TimedOut: false }).Select(r => r.JudgeResult!.PersonalityScore).DefaultIfEmpty(0).Average();

    /// <summary>
    /// Average meaning preservation score across all successfully judged results (0-5).
    /// Timed-out judge results are excluded.
    /// </summary>
    public double AverageMeaningScore =>
        Results.Where(r => r.JudgeResult is { TimedOut: false }).Select(r => r.JudgeResult!.MeaningScore).DefaultIfEmpty(0).Average();

    /// <summary>
    /// Results where meaning score is below 3 — the dangerous failures.
    /// Timed-out judge results are excluded so a judge outage isn't reported as meaning loss.
    /// </summary>
    public IReadOnlyList<PersonalityScenarioResult> MeaningFailures =>
        Results.Where(r => r.JudgeResult is { TimedOut: false } && r.JudgeResult.MeaningScore < 3).ToList();

    /// <summary>
    /// Results that failed because the model-under-test or judge call exceeded its
    /// deadline. Surfaced separately from scored failures so timeouts aren't conflated
    /// with genuine zero-score judgements.
    /// </summary>
    public IReadOnlyList<PersonalityScenarioResult> Timeouts =>
        Results.Where(r => r.TimedOut).ToList();
}
