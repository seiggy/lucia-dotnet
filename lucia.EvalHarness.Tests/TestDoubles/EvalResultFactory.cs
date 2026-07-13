using AgentEval.Models;
using lucia.EvalHarness.Evaluation;

namespace lucia.EvalHarness.Tests.TestDoubles;

internal static class EvalResultFactory
{
    public static ModelEvalResult Create(
        double? overallScore,
        double? taskCompletionScore = null,
        string? taskCompletionStatus = null) =>
        new()
        {
            ModelName = "model",
            AgentName = "agent",
            ToolSelectionScore = 100,
            ToolSuccessScore = 100,
            ToolEfficiencyScore = 100,
            TaskCompletionScore = taskCompletionScore,
            TaskCompletionStatus = taskCompletionStatus,
            TaskCompletionReason = taskCompletionStatus is null ? null : "Judge score is unavailable.",
            OverallScore = overallScore,
            OverallScoreStatus = overallScore.HasValue ? null : JudgeAvailability.Unavailable,
            TestCaseCount = 1,
            PassedCount = overallScore >= 70 ? 1 : 0,
            Performance = ModelPerformanceSummary.FromSnapshots("model", []),
            TestCaseResults = []
        };
}
