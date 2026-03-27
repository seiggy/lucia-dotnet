namespace lucia.EvalHarness.Personality;

/// <summary>
/// Aggregated report for a single model's personality eval run.
/// Contains all (scenario × profile) results and computed summaries.
/// </summary>
public sealed class PersonalityEvalReport
{
    public required string ModelName { get; init; }
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
}
