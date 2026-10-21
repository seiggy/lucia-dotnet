using AgentEval.Core;
using AgentEval.Models;
using FakeItEasy;
using lucia.Agents.Abstractions;
using lucia.EvalHarness.Configuration;
using lucia.EvalHarness.Evaluation;
using lucia.EvalHarness.Providers;

namespace lucia.EvalHarness.Tests;

public sealed class EvalRunnerToolSelectionTests
{
    [Fact]
    public async Task EvaluateRealAgentAsync_HeterogeneousMatchingCases_ScoreEachCaseAgainstItsOwnTools()
    {
        var runner = CreateRunner(testCase => [testCase.ExpectedTools!.Single()]);

        var result = await runner.EvaluateRealAgentAsync(
            "model",
            CreateAgent(),
            [
                new TestCase { Name = "light", Input = "turn on", ExpectedTools = ["turn_on"] },
                new TestCase { Name = "blinds", Input = "close blinds", ExpectedTools = ["close_blinds"] }
            ]);

        Assert.Equal(100, result.ToolSelectionScore);
        Assert.All(result.TestCaseResults, testCase => Assert.Equal(100, testCase.Score));
    }

    [Fact]
    public async Task EvaluateRealAgentAsync_WrongTool_IsDistinguishableFromMatchingCase()
    {
        var runner = CreateRunner(_ => ["turn_on"]);

        var result = await runner.EvaluateRealAgentAsync(
            "model",
            CreateAgent(),
            [
                new TestCase { Name = "matching", Input = "turn on", ExpectedTools = ["turn_on"] },
                new TestCase { Name = "wrong", Input = "close blinds", ExpectedTools = ["close_blinds"] }
            ]);

        Assert.Equal(100, result.TestCaseResults[0].Score);
        Assert.Equal(200d / 3, result.TestCaseResults[1].Score!.Value, precision: 6);
        Assert.True(result.TestCaseResults[0].Score > result.TestCaseResults[1].Score);
        Assert.False(result.TestCaseResults[1].Passed);
    }

    [Fact]
    public async Task EvaluateRealAgentAsync_NoToolCase_DoesNotInheritAnotherCasesExpectation()
    {
        var runner = CreateRunner(testCase =>
            testCase.Name == "tool" ? ["turn_on"] : []);

        var result = await runner.EvaluateRealAgentAsync(
            "model",
            CreateAgent(),
            [
                new TestCase { Name = "tool", Input = "turn on", ExpectedTools = ["turn_on"] },
                new TestCase { Name = "no-tool", Input = "say hello", ExpectedTools = [] }
            ]);

        Assert.Equal(100, result.ToolSelectionScore);
        Assert.Equal(2, result.PassedCount);
        Assert.All(result.TestCaseResults, testCase => Assert.Equal(100, testCase.Score));
    }

    [Fact]
    public async Task EvaluateRealAgentAsync_MissingRequiredTool_RemainsZeroSelectionScore()
    {
        var runner = CreateRunner(_ => []);

        var result = await runner.EvaluateRealAgentAsync(
            "model",
            CreateAgent(),
            [new TestCase { Name = "missing", Input = "turn on", ExpectedTools = ["turn_on"] }]);

        Assert.Equal(0, result.ToolSelectionScore);
        Assert.Equal(200d / 3, result.TestCaseResults.Single().Score!.Value, precision: 6);
        Assert.Equal(0, result.PassedCount);
    }

    private static EvalRunner CreateRunner(Func<TestCase, IReadOnlyList<string>> calledTools)
    {
        return new EvalRunner(new HarnessConfiguration(), judgeChatClient: null)
        {
            TestContextFactory = (testCase, _, _) =>
                Task.FromResult(CreateContext(testCase, calledTools(testCase)))
        };
    }

    private static (EvaluationContext Context, PerformanceSnapshot Performance) CreateContext(
        TestCase testCase,
        IReadOnlyList<string> calledTools)
    {
        var usage = new ToolUsageReport();
        for (var i = 0; i < calledTools.Count; i++)
        {
            usage.AddCall(new ToolCallRecord
            {
                Name = calledTools[i],
                CallId = $"call-{i}",
                Order = i + 1,
                Result = "ok"
            });
        }

        return (
            new EvaluationContext
            {
                Input = testCase.Input,
                Output = "done",
                ToolUsage = usage,
                ExpectedTools = testCase.ExpectedTools
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
}
