using AgentEval.Models;
using lucia.EvalHarness.Evaluation;
using lucia.EvalHarness.Optimization;
using lucia.EvalHarness.Tests.TestDoubles;
using Microsoft.Extensions.AI;

namespace lucia.EvalHarness.Tests;

public sealed class OptimizerAvailabilityTests
{
    [Fact]
    public async Task OptimizeAsync_UnavailableScores_AreNotSynthesizedAsZero()
    {
        var client = new ScriptedChatClient(_ => Task.FromResult(new ChatResponse(
            new ChatMessage(ChatRole.Assistant, """{"analysis":"ok","suggestions":[]}"""))));
        var optimizer = new PromptOptimizer(client);

        var result = await optimizer.OptimizeAsync(
            "agent",
            "model",
            "prompt",
            [EvalResultFactory.Create(null)],
            [EvalResultFactory.Create(null)]);

        Assert.Null(result.CurrentScore);
        Assert.Null(result.BaselineScore);
    }

    [Fact]
    public void Build_UnavailableCases_AreNotPresentedAsModelFailures()
    {
        var unavailable = new TestCaseResult
        {
            TestCaseId = "unavailable-case",
            Passed = false,
            Score = null,
            Latency = TimeSpan.Zero,
            JudgeStatus = JudgeAvailability.ProviderError
        };
        var result = new ModelEvalResult
        {
            ModelName = "model",
            AgentName = "agent",
            ToolSelectionScore = null,
            ToolSuccessScore = null,
            ToolEfficiencyScore = null,
            TaskCompletionScore = null,
            OverallScore = null,
            TestCaseCount = 1,
            ScoredTestCaseCount = 0,
            PassedCount = 0,
            Performance = ModelPerformanceSummary.FromSnapshots("model", []),
            TestCaseResults = [unavailable]
        };

        var prompt = OptimizationPromptBuilder.Build("agent", "model", "prompt", [result], null);

        Assert.DoesNotContain("## Failed Test Cases", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("unavailable-case", prompt, StringComparison.Ordinal);
    }
}
