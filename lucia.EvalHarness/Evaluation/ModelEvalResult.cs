using lucia.EvalHarness.Configuration;

namespace lucia.EvalHarness.Evaluation;

public sealed class ModelEvalResult
{
    public required string ModelName { get; init; }
    public required string AgentName { get; init; }
    public required double? ToolSelectionScore { get; init; }
    public required double? ToolSuccessScore { get; init; }
    public required double? ToolEfficiencyScore { get; init; }
    public required double? TaskCompletionScore { get; init; }
    public string? TaskCompletionStatus { get; init; }
    public string? TaskCompletionReason { get; init; }
    public required double? OverallScore { get; init; }
    public string? OverallScoreStatus { get; init; }
    public string? OverallScoreReason { get; init; }
    public required int TestCaseCount { get; init; }
    public required int PassedCount { get; init; }
    public required ModelPerformanceSummary Performance { get; init; }
    public required IReadOnlyList<TestCaseResult> TestCaseResults { get; init; }
    public ModelParameterProfile? ParameterProfile { get; init; }
}
