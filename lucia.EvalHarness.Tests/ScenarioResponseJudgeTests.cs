using System.Text.Json;
using FakeItEasy;
using lucia.EvalHarness.Configuration;
using lucia.EvalHarness.Evaluation;
using lucia.EvalHarness.Providers;
using lucia.EvalHarness.Tests.TestDoubles;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace lucia.EvalHarness.Tests;

public sealed class ScenarioResponseJudgeTests
{
    [Theory]
    [InlineData("I can turn lights on and adjust brightness, but weather is outside my role.", true)]
    [InlineData("Please ask a weather service; I only handle lighting.", true)]
    [InlineData("Sorry, I can't help with that.", true)]
    [InlineData("It will be sunny and 72 degrees today.", false)]
    public async Task WeatherScenario_UsesTheJudgeVerdictInsteadOfBannedWords(string response, bool verdict)
    {
        var judge = A.Fake<IChatClient>();
        IReadOnlyList<ChatMessage> judgeMessages = [];
        A.CallTo(() => judge.GetResponseAsync(A<IEnumerable<ChatMessage>>._, A<ChatOptions?>._, A<CancellationToken>._))
            .Invokes((IEnumerable<ChatMessage> messages, ChatOptions? _, CancellationToken _) =>
                judgeMessages = messages.ToList())
            .Returns(new ChatResponse(new ChatMessage(ChatRole.Assistant,
                verdict
                    ? """{"passed":true,"reason":"Appropriately declines or redirects the weather request."}"""
                    : """{"passed":false,"reason":"Provides a forecast instead of refusing."}""")));

        var result = await RunAsync(ScriptedChatClient.Returning(response), judge);
        var testCase = Assert.Single(result.TestCaseResults);

        Assert.Equal(verdict, testCase.Passed);
        Assert.Null(testCase.JudgeStatus);
        var judgeInput = Assert.Single(judgeMessages, message => message.Role == ChatRole.User);
        using var payload = JsonDocument.Parse(judgeInput.Text);
        Assert.Equal(response, payload.RootElement.GetProperty("response").GetString());
        if (!verdict)
        {
            Assert.Contains("Provides a forecast", testCase.FailureReason, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("missing", JudgeAvailability.NotConfigured)]
    [InlineData("invalid", JudgeAvailability.InvalidResponse)]
    [InlineData("provider", JudgeAvailability.ProviderError)]
    [InlineData("timeout", JudgeAvailability.Timeout)]
    public async Task WeatherScenario_UnavailableJudgeDoesNotPassOrFallBackToStringChecks(string failure, string status)
    {
        IChatClient? judge = failure switch
        {
            "missing" => null,
            "provider" => ScriptedChatClient.Throwing(_ => new HttpRequestException("provider failure")),
            "timeout" => ScriptedChatClient.Throwing(_ => new TimeoutException("judge deadline")),
            _ => ScriptedChatClient.Returning("""{"passed":"yes"}""")
        };

        var result = await RunAsync(ScriptedChatClient.Returning("Please ask a weather service."), judge);
        var testCase = Assert.Single(result.TestCaseResults);

        Assert.False(testCase.Passed);
        Assert.Null(testCase.Score);
        Assert.Equal(status, testCase.JudgeStatus);
        Assert.Equal(status, result.TaskCompletionStatus);
    }

    [Fact]
    public async Task WeatherScenario_StillRejectsToolCallsWhenJudgeAcceptsTheResponse()
    {
        var calls = 0;
        var agent = new ScriptedChatClient(_ => Task.FromResult(
            ++calls == 1
                ? new ChatResponse(new ChatMessage(ChatRole.Assistant,
                    [new FunctionCallContent("call-1", "ControlLights", new Dictionary<string, object?>
                    {
                        ["searchTerms"] = new[] { "kitchen" },
                        ["state"] = "on"
                    })]))
                : new ChatResponse(new ChatMessage(ChatRole.Assistant, "Weather is outside my role."))));
        var judge = ScriptedChatClient.Returning("""{"passed":true,"reason":"Appropriate refusal."}""");

        var result = await RunAsync(agent, judge);
        var testCase = Assert.Single(result.TestCaseResults);

        Assert.False(testCase.Passed);
        Assert.Contains("Expected no tool calls", testCase.FailureReason, StringComparison.Ordinal);
        Assert.Null(testCase.JudgeStatus);
    }

    [Fact]
    public async Task WeatherScenario_PropagatesCallerCancellationDuringJudging()
    {
        using var cancellation = new CancellationTokenSource();
        var judge = new ScriptedChatClient(_ =>
        {
            cancellation.Cancel();
            return Task.FromException<ChatResponse>(new OperationCanceledException(cancellation.Token));
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            RunAsync(ScriptedChatClient.Returning("Weather is outside my role."), judge, cancellation.Token));
    }

    private static async Task<ModelEvalResult> RunAsync(
        IChatClient agentClient, IChatClient? judgeClient, CancellationToken cancellationToken = default)
    {
        var scenario = ScenarioLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "light-agent.yaml"))
            .Single(scenario => scenario.Id == "out_of_domain_weather");
        using var loggerFactory = LoggerFactory.Create(_ => { });
        await using var factory = new RealAgentFactory(
            "http://unused:11434",
            Path.Combine(AppContext.BaseDirectory, "TestData", "ha-snapshot.json"),
            loggerFactory)
        {
            EnableTracing = true,
            ChatClientCreator = (_, _, _) => agentClient
        };
        var agent = await factory.CreateLightAgentAsync("test-model");

        return await new EvalRunner(new HarnessConfiguration(), judgeClient).EvaluateScenariosAsync(
            "test-model", agent, [scenario], factory.HomeAssistantClient, factory.EntityLocationService,
            ct: cancellationToken);
    }
}
