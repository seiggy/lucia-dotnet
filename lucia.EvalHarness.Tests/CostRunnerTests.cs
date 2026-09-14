using FakeItEasy;
using lucia.Agents.Abstractions;
using lucia.EvalHarness.Configuration;
using lucia.EvalHarness.Evaluation;
using lucia.EvalHarness.Personality;
using lucia.EvalHarness.Providers;
using lucia.EvalHarness.Tests.TestDoubles;
using lucia.HomeAssistant.Services;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucia.EvalHarness.Tests;

public sealed class CostRunnerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealAgent_ToolLoopCapturesEveryCallAndResetsEachTest(bool scenarios)
    {
        var call = 0;
        using var raw = Client(new ScriptedChatClient(_ => Task.FromResult(new ChatResponse(
            ++call % 2 == 1
                ? new ChatMessage(ChatRole.Assistant, [new FunctionCallContent($"call-{call}", "turn_on")])
                : new ChatMessage(ChatRole.Assistant, "done"))
        {
            Usage = new UsageDetails { InputTokenCount = 100, OutputTokenCount = 20 }
        })));
        using var chat = raw.AsBuilder().UseFunctionInvocation().Build();
        var aiAgent = new ChatClientAgent(chat, new ChatClientAgentOptions
        {
            Name = "agent",
            ChatOptions = new ChatOptions { Tools = [AIFunctionFactory.Create(() => "done", "turn_on")] }
        }, NullLoggerFactory.Instance);
        var agent = A.Fake<ILuciaAgent>();
        A.CallTo(() => agent.GetAIAgent()).Returns(aiAgent);
        await using var instance = new RealAgentInstance
        {
            AgentName = "agent",
            Agent = agent,
            DatasetFile = "unused",
            OwnedChatClient = chat
        };
        var runner = new EvalRunner(new HarnessConfiguration(), null);
        var result = scenarios
            ? await runner.EvaluateScenariosAsync("model", instance,
                [new() { Id = "first", UserPrompt = "turn on" }, new() { Id = "second", UserPrompt = "turn on" }],
                A.Fake<IHomeAssistantClient>())
            : await runner.EvaluateRealAgentAsync("model", instance,
                [new() { Name = "first", Input = "turn on" }, new() { Name = "second", Input = "turn on" }]);

        Assert.Equal(4, call);
        Assert.All(result.TestCaseResults, test =>
        {
            Assert.Equal(2, test.Cost.CallCount);
            Assert.Equal(200, test.Cost.InputTokens);
            Assert.Equal(40, test.Cost.OutputTokens);
            Assert.Equal(0.00028m, test.Cost.EstimatedUsd);
        });
        Assert.Equal(0.00056m, result.Cost.EstimatedUsd);
    }

    [Fact]
    public async Task Personality_JudgeUsingSameClientCannotAddToTestCost()
    {
        var calls = 0;
        using var client = Client(new ScriptedChatClient(_ => Task.FromResult(
            new ChatResponse(new ChatMessage(ChatRole.Assistant, ++calls % 2 == 1
                ? "The light is on."
                : """{"personalityScore":5,"meaningScore":5,"personalityReason":"ok","meaningReason":"ok"}"""))
            {
                Usage = new UsageDetails { InputTokenCount = 100, OutputTokenCount = 20 }
            })));

        var report = await new PersonalityEvalRunner().RunAsync(client, "model", client, "judge",
            [Scenario()], [new() { Id = "plain", Name = "Plain", Instructions = "Be brief." }]);
        Assert.True(calls >= 2);
        Assert.Equal(5, report.AverageCombinedScore);
        Assert.Equal(1, report.Cost.CallCount);
        Assert.Equal(0.00014m, report.Cost.EstimatedUsd);
    }

    [Fact]
    public async Task Personality_ProviderFailureKeepsUnknownUsageAndAvailability()
    {
        using var client = Client(ScriptedChatClient.Throwing(_ => new HttpRequestException("failed")));
        var report = await new PersonalityEvalRunner().RunAsync(client, "model", null, "none",
            [Scenario()], [new() { Id = "plain", Name = "Plain", Instructions = "Be brief." }]);
        var result = Assert.Single(report.Results);
        Assert.Null(result.Score);
        Assert.Null(result.Cost.EstimatedUsd);
        Assert.Equal("missing_usage", result.Cost.CostStatus);
        Assert.Equal(1, result.Cost.CallCount);
    }

    private static InferenceCostChatClient Client(IChatClient inner) => new(inner, new InferenceBackend
    {
        Name = "cloud",
        Endpoint = "https://example.test",
        Type = InferenceBackendType.OpenRouter,
        ModelPricing = new() { ["model"] = new()
        {
            InputUsdPerMillionTokens = 1m,
            OutputUsdPerMillionTokens = 2m
        } }
    }, "model");

    private static PersonalityEvalScenario Scenario() => new()
    {
        Id = "cost-test",
        Category = "test",
        Description = "Cost capture",
        SkillId = "lights",
        Action = "turn_on",
        AgentResponse = "The light is on.",
        PersonalityPrompt = "Be brief.",
        Expectations = new()
    };
}
