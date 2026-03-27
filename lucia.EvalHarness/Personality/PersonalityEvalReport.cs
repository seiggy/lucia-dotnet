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
    public int PassCount => Results.Count(r => r.Passed);
    public int FailCount => Results.Count(r => !r.Passed);
    public double PassRate => TotalCombinations > 0 ? (double)PassCount / TotalCombinations * 100 : 0;

    public IReadOnlyList<string> ScenarioIds =>
        Results.Select(r => r.ScenarioId).Distinct().ToList();

    public IReadOnlyList<string> ProfileIds =>
        Results.Select(r => r.ProfileId).Distinct().ToList();

    /// <summary>
    /// Average personality adherence score across all results (0-5).
    /// </summary>
    public double AveragePersonalityScore =>
        Results.Where(r => r.JudgeResult is not null).Select(r => r.JudgeResult!.PersonalityScore).DefaultIfEmpty(0).Average();

    /// <summary>
    /// Average meaning preservation score across all results (0-5).
    /// </summary>
    public double AverageMeaningScore =>
        Results.Where(r => r.JudgeResult is not null).Select(r => r.JudgeResult!.MeaningScore).DefaultIfEmpty(0).Average();

    /// <summary>
    /// Results where meaning score is below 3 — the dangerous failures.
    /// </summary>
    public IReadOnlyList<PersonalityScenarioResult> MeaningFailures =>
        Results.Where(r => r.JudgeResult is not null && r.JudgeResult.MeaningScore < 3).ToList();
}
