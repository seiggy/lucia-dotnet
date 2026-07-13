using System.Net.Http;
using System.Text.Json;
using AgentEval.Core;
using AgentEval.Models;
using FakeItEasy;
using lucia.Agents.Abstractions;
using lucia.EvalHarness.Configuration;
using lucia.EvalHarness.Evaluation;
using lucia.EvalHarness.Providers;
using lucia.EvalHarness.Tests.TestDoubles;
using lucia.HomeAssistant.Models;
using lucia.HomeAssistant.Services;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

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
    public async Task EvaluateRealAgentAsync_GenuineZeroToolScore_RemainsAvailable()
    {
        var runner = CreateRunner(null, (_, _, _) => Task.FromResult((
            new EvaluationContext
            {
                Input = "turn on",
                Output = "done",
                ToolUsage = new ToolUsageReport(),
                ExpectedTools = ["turn_on"]
            },
            new PerformanceSnapshot { TotalDuration = TimeSpan.FromMilliseconds(1) })));

        var result = await runner.EvaluateRealAgentAsync(
            "model",
            CreateAgent(),
            [new TestCase { Name = "case", Input = "turn on", ExpectedTools = ["turn_on"] }]);

        Assert.Equal(0, result.ToolSelectionScore);
        Assert.NotNull(result.OverallScore);
        Assert.Equal(1, result.TestCaseCount);
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

    [Fact]
    public async Task EvaluateRealAgentAsync_PartialProviderFailure_ExcludesUnavailableExecution()
    {
        var runner = CreateRunner(null, (testCase, _, _) =>
            testCase.Name == "unavailable"
                ? Task.FromException<(EvaluationContext, PerformanceSnapshot)>(
                    new HttpRequestException("provider unavailable"))
                : Task.FromResult(SuccessfulContext()));

        var result = await runner.EvaluateRealAgentAsync(
            "model",
            CreateAgent(),
            [
                new TestCase { Name = "unavailable", Input = "first" },
                new TestCase { Name = "available", Input = "second", ExpectedTools = ["turn_on"] }
            ]);

        Assert.Equal(1, result.TestCaseCount);
        Assert.Equal(1, result.PassedCount);
        Assert.Equal(100, result.ToolSelectionScore);
        Assert.Equal(100, result.ToolSuccessScore);
        Assert.Equal(100, result.ToolEfficiencyScore);
        Assert.Equal(2, result.TestCaseResults.Count);
        Assert.Null(result.TestCaseResults[0].Score);
    }

    [Fact]
    public async Task EvaluateRealAgentAsync_AllProviderFailures_DoNotEmitZeroToolScores()
    {
        var runner = CreateRunner(null, (_, _, _) =>
            Task.FromException<(EvaluationContext, PerformanceSnapshot)>(
                new HttpRequestException("provider unavailable")));

        var result = await runner.EvaluateRealAgentAsync(
            "model",
            CreateAgent(),
            [new TestCase { Name = "unavailable", Input = "first" }]);

        Assert.Equal(0, result.TestCaseCount);
        Assert.Equal(0, result.PassedCount);
        Assert.True((object?)result.ToolSelectionScore is null);
        Assert.True((object?)result.ToolSuccessScore is null);
        Assert.True((object?)result.ToolEfficiencyScore is null);
        Assert.Null(result.OverallScore);
    }

    [Theory]
    [InlineData("json")]
    [InlineData("format")]
    [InlineData("io")]
    [InlineData("programming")]
    public async Task EvaluateRealAgentAsync_UnexpectedExecutionException_Propagates(string failure)
    {
        Exception expected = failure switch
        {
            "json" => new JsonException("application serialization bug"),
            "format" => new FormatException("application format bug"),
            "io" => new IOException("application I/O bug"),
            _ => new InvalidOperationException("programming bug")
        };
        var runner = CreateRunner(null, (_, _, _) =>
            Task.FromException<(EvaluationContext, PerformanceSnapshot)>(
                expected));

        var actual = await Assert.ThrowsAnyAsync<Exception>(() =>
            runner.EvaluateRealAgentAsync(
                "model",
                CreateAgent(),
                [new TestCase { Name = "case", Input = "turn on" }]));

        Assert.Equal(expected.GetType(), actual.GetType());
    }

    [Fact]
    public async Task EvaluateScenariosAsync_CallerCancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        var homeAssistant = A.Fake<IHomeAssistantClient>();
        A.CallTo(() => homeAssistant.SetEntityStateAsync(
                A<string>._,
                A<string>._,
                A<Dictionary<string, object>?>._,
                A<CancellationToken>._))
            .ReturnsLazily(() =>
            {
                cts.Cancel();
                return Task.FromException<HomeAssistantState>(
                    new OperationCanceledException(cts.Token));
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new EvalRunner(new HarnessConfiguration(), null).EvaluateScenariosAsync(
                "model",
                CreateScenarioAgent(),
                [CreateScenario()],
                homeAssistant,
                ct: cts.Token));
    }

    [Fact]
    public async Task EvaluateScenariosAsync_UnexpectedException_Propagates()
    {
        var homeAssistant = A.Fake<IHomeAssistantClient>();
        A.CallTo(() => homeAssistant.SetEntityStateAsync(
                A<string>._,
                A<string>._,
                A<Dictionary<string, object>?>._,
                A<CancellationToken>._))
            .ThrowsAsync(new JsonException("application serialization bug"));

        await Assert.ThrowsAsync<JsonException>(() =>
            new EvalRunner(new HarnessConfiguration(), null).EvaluateScenariosAsync(
                "model",
                CreateScenarioAgent(),
                [CreateScenario()],
                homeAssistant));
    }

    [Fact]
    public async Task EvaluateScenariosAsync_ProviderFailure_IsUnavailableWithoutZeroScores()
    {
        var homeAssistant = A.Fake<IHomeAssistantClient>();
        A.CallTo(() => homeAssistant.SetEntityStateAsync(
                A<string>._,
                A<string>._,
                A<Dictionary<string, object>?>._,
                A<CancellationToken>._))
            .ThrowsAsync(new HttpRequestException("provider unavailable"));

        var result = await new EvalRunner(new HarnessConfiguration(), null).EvaluateScenariosAsync(
            "model",
            CreateScenarioAgent(),
            [CreateScenario()],
            homeAssistant);

        Assert.Equal(0, result.TestCaseCount);
        Assert.Null(result.TestCaseResults.Single().Score);
        Assert.Equal("Provider request failed.", result.TestCaseResults.Single().FailureReason);
        Assert.True((object?)result.ToolSelectionScore is null);
        Assert.True((object?)result.ToolSuccessScore is null);
        Assert.True((object?)result.ToolEfficiencyScore is null);
        Assert.Null(result.OverallScore);
    }

    private static EvalRunner CreateRunner(
        IChatClient? judge,
        Func<TestCase, int, CancellationToken,
            Task<(EvaluationContext Context, PerformanceSnapshot Performance)>>? contextFactory = null)
    {
        return new EvalRunner(new HarnessConfiguration(), judge)
        {
            TestContextFactory = contextFactory ?? ((_, _, _) => Task.FromResult(SuccessfulContext()))
        };
    }

    private static (EvaluationContext Context, PerformanceSnapshot Performance) SuccessfulContext()
    {
        var usage = new ToolUsageReport();
        usage.AddCall(new ToolCallRecord
        {
            Name = "turn_on",
            CallId = "1",
            Order = 1,
            Result = "ok"
        });

        return (
            new EvaluationContext
            {
                Input = "turn on",
                Output = "done",
                ToolUsage = usage,
                ExpectedTools = ["turn_on"]
            },
            new PerformanceSnapshot { TotalDuration = TimeSpan.FromMilliseconds(1) });
    }

    private static RealAgentInstance CreateAgent() =>
        new()
        {
            AgentName = "agent",
            Agent = A.Fake<ILuciaAgent>(),
            DatasetFile = "unused"
        };

    private static RealAgentInstance CreateScenarioAgent()
    {
        var aiAgent = new ChatClientAgent(
            Responding("done"),
            new ChatClientAgentOptions
            {
                Id = "agent",
                Name = "agent"
            },
            NullLoggerFactory.Instance);
        var agent = A.Fake<ILuciaAgent>();
        A.CallTo(() => agent.GetAIAgent()).Returns(aiAgent);
        return new RealAgentInstance
        {
            AgentName = "agent",
            Agent = agent,
            DatasetFile = "unused"
        };
    }

    private static TestScenario CreateScenario() =>
        new()
        {
            Id = "scenario",
            UserPrompt = "turn on",
            InitialState =
            {
                ["light.test"] = new EntitySetup { State = "off" }
            }
        };

    private static ScriptedChatClient Responding(string text) =>
        new(_ => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text))));

    private static ScriptedChatClient Throwing(Exception exception) =>
        new(_ => Task.FromException<ChatResponse>(exception));
}
