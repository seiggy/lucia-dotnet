namespace lucia.EvalHarness.Evaluation;

public sealed class ScenarioValidationResult
{
    public required string ScenarioId { get; init; }
    public required bool Passed { get; init; }
    public required double Score { get; init; }
    public required List<string> Issues { get; init; }
    public required List<string> Successes { get; init; }

    public string Summary => Passed
        ? $"PASS ({Successes.Count} checks passed)"
        : $"FAIL: {string.Join("; ", Issues)}";
}
