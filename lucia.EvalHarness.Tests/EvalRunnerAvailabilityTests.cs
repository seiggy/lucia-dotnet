using System.Net.Http;
using AgentEval.Core;
using AgentEval.Models;
using FakeItEasy;
using lucia.Agents.Abstractions;
using lucia.EvalHarness.Configuration;
using lucia.EvalHarness.Evaluation;
using lucia.EvalHarness.Providers;
using lucia.EvalHarness.Tests.TestDoubles;
using Microsoft.Extensions.AI;

namespace lucia.EvalHarness.Tests;

public sealed class EvalRunnerAvailabilityTests
{
    [Theory]
    [InlineData("provider", JudgeAvailability.ProviderError)]
    [InlineData("timeout", JudgeAvailability.Timeout)]
    [InlineData("invalid", JudgeAvailability.InvalidResponse)]
    public async Task EvaluateRealAgentAsync_JudgeUnavailable_PreservesToolMetrics(
        string failure,
        string expectedStatus)
    {
        var judge = failure switch
        {
            "provider" => Throwing(new HttpRequestException("secret provider detail")),
            "timeout" => Throwing(new OperationCanceledException("secret timeout detail")),
            _ => Responding("not json")
        };
        var runner = CreateRunner(judge);

        var result = await runner.EvaluateRealAgentAsync(
            "model",
            CreateAgent(),
            [new TestCase { Name = "case", Input = "turn on", ExpectedTools = ["turn_on"] }]);

        Assert.Equal(100, result.ToolSelectionScore);
        Assert.Equal(100, result.ToolSuccessScore);
        Assert.Equal(100, result.ToolEfficiencyScore);
        Assert.Null(result.TaskCompletionScore);
        Assert.Equal(expectedStatus, result.TaskCompletionStatus);
        Assert.DoesNotContain("secret", result.TaskCompletionReason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(100, result.OverallScore);
        Assert.Equal(1, result.PassedCount);
    }

    [Fact]
    public async Task EvaluateRealAgentAsync_NoJudge_UsesNotConfiguredStatus()
    {
        var runner = CreateRunner(null);

        var result = await runner.EvaluateRealAgentAsync(
            "model",
            CreateAgent(),
            [new TestCase { Name = "case", Input = "turn on", ExpectedTools = ["turn_on"] }]);

        Assert.Null(result.TaskCompletionScore);
        Assert.Equal(JudgeAvailability.NotConfigured, result.TaskCompletionStatus);
        Assert.Equal(100, result.OverallScore);
    }

    [Fact]
    public async Task EvaluateRealAgentAsync_GenuineZero_RemainsAvailable()
    {
        var runner = CreateRunner(Responding("""{"score":0,"reasoning":"genuine zero"}"""));

        var result = await runner.EvaluateRealAgentAsync(
            "model",
            CreateAgent(),
            [new TestCase { Name = "case", Input = "turn on", ExpectedTools = ["turn_on"] }]);

        Assert.Equal(0, result.TaskCompletionScore);
        Assert.Null(result.TaskCompletionStatus);
        Assert.Equal(75, result.OverallScore);
    }

    [Fact]
    public async Task EvaluateRealAgentAsync_CallerCancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        var judge = new ScriptedChatClient(_ =>
        {
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        });
        var runner = CreateRunner(judge);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            runner.EvaluateRealAgentAsync(
                "model",
                CreateAgent(),
                [new TestCase { Name = "case", Input = "turn on", ExpectedTools = ["turn_on"] }],
                ct: cts.Token));
    }

    private static EvalRunner CreateRunner(IChatClient? judge)
    {
        var usage = new ToolUsageReport();
        usage.AddCall(new ToolCallRecord
        {
            Name = "turn_on",
            CallId = "1",
            Order = 1,
            Result = "ok"
        });

        return new EvalRunner(new HarnessConfiguration(), judge)
        {
            TestContextFactory = (_, _, _) => Task.FromResult((
                new EvaluationContext
                {
                    Input = "turn on",
                    Output = "done",
                    ToolUsage = usage,
                    ExpectedTools = ["turn_on"]
                },
                new PerformanceSnapshot { TotalDuration = TimeSpan.FromMilliseconds(1) }))
        };
    }

    private static RealAgentInstance CreateAgent() =>
        new()
        {
            AgentName = "agent",
            Agent = A.Fake<ILuciaAgent>(),
            DatasetFile = "unused"
        };

    private static ScriptedChatClient Responding(string text) =>
        new(_ => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text))));

    private static ScriptedChatClient Throwing(Exception exception) =>
        new(_ => Task.FromException<ChatResponse>(exception));
}
