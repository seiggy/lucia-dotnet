namespace lucia.Evals;

/// <summary>
/// Aggregated results from a scenario run.
/// </summary>
public sealed record ScenarioRunReport
{
    public required IReadOnlyList<(EvalScenario Scenario, EvalScenarioResult Result)> Results { get; init; }
    public required TimeSpan TotalDuration { get; init; }

    public int Passed => Results.Count(r => r.Result.Passed);
    public int Failed => Results.Count(r => !r.Result.Passed && !r.Result.Skipped);
    public int Skipped => Results.Count(r => r.Result.Skipped);
    public int Total => Results.Count;
}
