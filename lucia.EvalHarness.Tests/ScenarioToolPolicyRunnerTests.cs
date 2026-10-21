using FakeItEasy;
using lucia.Agents.Abstractions;
using lucia.EvalHarness.Configuration;
using lucia.EvalHarness.Evaluation;
using lucia.EvalHarness.Providers;
using lucia.EvalHarness.Tests.TestDoubles;
using lucia.HomeAssistant.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace lucia.EvalHarness.Tests;

public sealed class ScenarioToolPolicyRunnerTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task EvaluateScenariosAsync_PolicyWithoutPositiveExpectations_RequiresTracing(bool strict, bool forbidden)
    {
        var scenario = new TestScenario
        {
            Id = "policy_only",
            UserPrompt = "Say hello",
            ExpectedAgent = "agent",
            RejectUnlistedToolCalls = strict,
            ForbiddenTools = forbidden ? ["UnlockDoor"] : []
        };
        await using var instance = new RealAgentInstance
        {
            AgentName = "agent",
            Agent = A.Fake<ILuciaAgent>(),
            DatasetFile = "unused",
            ScenarioExecutor = (_, _) => Task.FromResult("Hello"),
            RoutedAgentId = () => "agent"
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new EvalRunner(new HarnessConfiguration(), null).EvaluateScenariosAsync(
                "test-model", instance, [scenario], A.Fake<IHomeAssistantClient>()));

        Assert.Contains("requires conversation tracing", exception.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EvaluateScenariosAsync_ForbiddenEffect_FailsDespitePassingJudgeAndHighScore(bool strict)
    {
        var round = 0;
        var client = new ScriptedChatClient(_ => Task.FromResult(++round switch
        {
            1 => ToolResponse("lookup", "GetLightsState", new() { ["searchTerms"] = new[] { "kitchen" } }),
            2 => ToolResponse("effect", "ControlLights", new()
            {
                ["searchTerms"] = new[] { "kitchen" },
                ["state"] = "on"
            }),
            _ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "The kitchen light was off."))
        }));
        using var loggerFactory = LoggerFactory.Create(_ => { });
        await using var factory = new RealAgentFactory(
            "http://unused:11434",
            Path.Combine(AppContext.BaseDirectory, "TestData", "ha-snapshot.json"),
            loggerFactory)
        {
            EnableTracing = true,
            ChatClientCreator = (_, _, _) => client
        };
        var instance = await factory.CreateLightAgentAsync("test-model");
        var scenario = new TestScenario
        {
            Id = "query_must_not_control",
            UserPrompt = "Is the kitchen light on? Do not change it.",
            RejectUnlistedToolCalls = strict,
            ForbiddenTools = ["ControlLights"],
            ExpectedToolCalls = [new ExpectedToolCall { Tool = "GetLightsState" }],
            InitialState = new()
            {
                ["light.kitchen_lights_light"] = new EntitySetup
                {
                    State = "off",
                    Attributes = new() { ["friendly_name"] = "Kitchen Lights light" }
                }
            },
            ExpectedFinalState = new()
            {
                ["light.kitchen_lights_light"] = new EntityStateAssertion { State = "off" }
            },
            // Keep the numeric score above 70 even with policy and state failures.
            ResponseMustContain = Enumerable.Repeat("kitchen", 10).ToList(),
            ResponseCriteria = "Answer the light status question."
        };
        var judge = ScriptedChatClient.Returning("""{"passed":true,"reason":"Answers the question."}""");

        var result = await new EvalRunner(new HarnessConfiguration(), judge).EvaluateScenariosAsync(
            "test-model", instance, [scenario], factory.HomeAssistantClient, factory.EntityLocationService);
        var testCase = Assert.Single(result.TestCaseResults);

        Assert.Contains("Forbidden tool call 'ControlLights'", testCase.FailureReason);
        Assert.Contains("expected state 'off' but got 'on'", testCase.FailureReason);
        Assert.False(testCase.Passed);
        Assert.Equal(0, result.PassedCount);
        Assert.True(testCase.Score > 70);
        Assert.Null(testCase.JudgeStatus);
        Assert.Contains(testCase.ToolCalls!, call => call.ToolName == "ControlLights");
        var state = await factory.HomeAssistantClient.GetEntityStateAsync("light.kitchen_lights_light");
        Assert.Equal("on", state!.State);
    }

    private static ChatResponse ToolResponse(string callId, string name, Dictionary<string, object?> arguments) =>
        new(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent(callId, name, arguments)]));
}
