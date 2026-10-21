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
    [Fact]
    public async Task EvaluateScenariosAsync_OfferedForbiddenToolNotCalled_PassesUsingActualInventory()
    {
        using var loggerFactory = LoggerFactory.Create(_ => { });
        await using var factory = new RealAgentFactory(
            "http://unused:11434",
            Path.Combine(AppContext.BaseDirectory, "TestData", "ha-snapshot.json"),
            loggerFactory)
        {
            EnableTracing = true,
            ChatClientCreator = (_, _, _) => ScriptedChatClient.Returning("Hello")
        };
        var instance = await factory.CreateLightAgentAsync("test-model");
        var scenario = new TestScenario
        {
            Id = "no_forbidden_effect",
            UserPrompt = "Say hello without changing the lights.",
            ForbiddenTools = ["ControlLights"]
        };

        var result = await new EvalRunner(new HarnessConfiguration(), null).EvaluateScenariosAsync(
            "test-model", instance, [scenario], factory.HomeAssistantClient, factory.EntityLocationService);
        var testCase = Assert.Single(result.TestCaseResults);

        Assert.Contains("ControlLights", instance.Tracer!.AvailableToolNames!);
        Assert.True(testCase.Passed);
        Assert.Equal(100d, testCase.Score);
        Assert.Equal(100d, result.OverallScore);
        Assert.Null(testCase.FailureReason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EvaluateScenariosAsync_InconclusivePolicy_RemainsUnscoredWhenJudgePasses(bool inventoryKnown)
    {
        using var tracer = new ConversationTracer(ScriptedChatClient.Returning("Hello"));
        await using var instance = new RealAgentInstance
        {
            AgentName = "agent",
            Agent = A.Fake<ILuciaAgent>(),
            DatasetFile = "unused",
            Tracer = tracer,
            RoutedAgentId = () => "agent",
            ScenarioExecutor = async (prompt, ct) =>
            {
                var options = inventoryKnown
                    ? new ChatOptions { Tools = [AIFunctionFactory.Create(() => "ok", "ListScenes")] }
                    : null;
                var response = await tracer.GetResponseAsync([new ChatMessage(ChatRole.User, prompt)], options, ct);
                return response.Text!;
            }
        };
        var scenario = new TestScenario
        {
            Id = "inconclusive_policy",
            UserPrompt = "Say hello",
            ExpectedAgent = "agent",
            ForbiddenTools = ["UnlockDoor"],
            ResponseCriteria = "Greet the user."
        };
        var judge = ScriptedChatClient.Returning("""{"passed":true,"reason":"Greets the user."}""");

        var result = await new EvalRunner(new HarnessConfiguration(), judge).EvaluateScenariosAsync(
            "test-model", instance, [scenario], A.Fake<IHomeAssistantClient>());
        var testCase = Assert.Single(result.TestCaseResults);

        Assert.False(testCase.Passed);
        Assert.Null(testCase.Score);
        Assert.Null(result.OverallScore);
        Assert.Null(testCase.JudgeStatus);
        Assert.Contains("Inconclusive", testCase.FailureReason);
        Assert.Contains("NeverCallTool(UnlockDoor)", testCase.FailureReason);
    }

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
