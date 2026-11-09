using AgentEval.Output;

namespace lucia.EvalHarness.Evaluation;

public sealed class ScenarioValidationResult
{
    public required string ScenarioId { get; init; }
    public required bool Passed { get; init; }
    public required double? Score { get; init; }
    public required List<string> Issues { get; init; }
    public required List<string> Successes { get; init; }
    public IReadOnlyList<AssertionResult> ToolPolicyResults { get; init; } = [];

    public string Summary
    {
        get
        {
            if (Passed)
            {
                return $"PASS ({Successes.Count} checks passed)";
            }

            var messages = new List<string>();
            if (Issues.Count > 0)
            {
                messages.Add($"FAIL: {string.Join("; ", Issues)}");
            }

            messages.AddRange(ToolPolicyResults.Where(result => result.IsInconclusive)
                .Select(result => $"Inconclusive {result.Assertion}: {result.Message}"));
            return string.Join("; ", messages);
        }
    }
}
