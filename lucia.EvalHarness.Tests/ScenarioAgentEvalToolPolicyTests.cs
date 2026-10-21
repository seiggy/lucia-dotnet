using AgentEval.Output;
using FakeItEasy;
using lucia.EvalHarness.Evaluation;
using lucia.HomeAssistant.Services;

namespace lucia.EvalHarness.Tests;

public sealed class ScenarioAgentEvalToolPolicyTests
{
    [Theory]
    [InlineData("available", AssertionOutcome.Passed)]
    [InlineData("unavailable", AssertionOutcome.Inconclusive)]
    [InlineData("unknown", AssertionOutcome.Inconclusive)]
    public async Task ValidateAsync_AbsentForbiddenTool_PreservesAgentEvalEvidenceOutcome(
        string inventory, AssertionOutcome expectedOutcome)
    {
        var scenario = new TestScenario
        {
            Id = "read_only",
            UserPrompt = "List scenes",
            ExpectedToolCalls = [new ExpectedToolCall { Tool = "ListScenes" }],
            ForbiddenTools = ["ActivateSceneAsync"]
        };
        IReadOnlyCollection<string>? available = inventory switch
        {
            "available" => ["ListScenes", "ActivateScene"],
            "unavailable" => ["ListScenes"],
            _ => null
        };

        var result = await ScenarioValidator.ValidateAsync(scenario,
            [new ConversationTurn { Role = "assistant", ToolCalls = [new ToolCallInfo { Name = "ListScenes" }] }],
            A.Fake<IHomeAssistantClient>(), available);

        var policy = Assert.Single(result.ToolPolicyResults);
        Assert.Equal("NeverCallTool(ActivateScene)", policy.Assertion);
        Assert.Equal(expectedOutcome, policy.Outcome);
        Assert.Equal(expectedOutcome == AssertionOutcome.Passed, result.Passed);
        if (expectedOutcome == AssertionOutcome.Inconclusive)
        {
            Assert.Null(result.Score);
            Assert.Contains("Inconclusive", result.Summary);
            Assert.Empty(result.Issues);
        }
        else
        {
            Assert.Equal(100d, result.Score);
        }
    }

    [Fact]
    public async Task ValidateAsync_ObservedForbiddenTool_IsAgentEvalFailureEvenWithoutInventory()
    {
        var scenario = new TestScenario
        {
            Id = "observed_violation",
            UserPrompt = "List scenes",
            ForbiddenTools = ["ActivateScene"]
        };

        var result = await ScenarioValidator.ValidateAsync(scenario,
            [new ConversationTurn { Role = "assistant", ToolCalls = [new ToolCallInfo { Name = "ActivateSceneAsync" }] }],
            A.Fake<IHomeAssistantClient>());

        var policy = Assert.Single(result.ToolPolicyResults);
        Assert.Equal(AssertionOutcome.Failed, policy.Outcome);
        Assert.Equal("NeverCallTool(ActivateScene)", policy.Assertion);
        Assert.False(result.Passed);
        Assert.Contains(result.Issues, issue => issue.Contains("[0] Forbidden tool call 'ActivateSceneAsync'"));
    }
}
