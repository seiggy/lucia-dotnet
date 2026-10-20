using FakeItEasy;
using lucia.Agents.Abstractions;
using lucia.EvalHarness.Configuration;
using lucia.EvalHarness.Evaluation;
using lucia.EvalHarness.Providers;
using lucia.EvalHarness.Tests.TestDoubles;
using lucia.HomeAssistant.Services;
using Microsoft.Agents.AI;

namespace lucia.EvalHarness.Tests;

public sealed class ScenarioSessionIsolationTests
{
    [Fact]
    public async Task EvaluateScenariosAsync_StartsEachScenarioWithoutEarlierConversation()
    {
        var model = new CapturingChatClient();
        var tracer = new ConversationTracer(model);
        var aiAgent = new ChatClientAgent(tracer, new ChatClientAgentOptions { Name = "isolation-test" });
        var luciaAgent = A.Fake<ILuciaAgent>();
        A.CallTo(() => luciaAgent.GetAIAgent()).Returns(aiAgent);
        var instance = new RealAgentInstance
        {
            AgentName = "IsolationAgent",
            Agent = luciaAgent,
            DatasetFile = "TestData/music-agent.yaml",
            Tracer = tracer
        };
        TestScenario[] scenarios =
        [
            new() { Id = "first", UserPrompt = "first prompt" },
            new() { Id = "second", UserPrompt = "second prompt" }
        ];
        var runner = new EvalRunner(new HarnessConfiguration(), judgeChatClient: null);

        var result = await runner.EvaluateScenariosAsync(
            "test-model",
            instance,
            scenarios,
            A.Fake<IHomeAssistantClient>());

        Assert.Equal(2, result.PassedCount);
        Assert.DoesNotContain(model.CapturedMessages, message => message.Text.Contains("first prompt"));
        Assert.DoesNotContain(tracer.Turns, turn => turn.Content?.Contains("first prompt") == true);
    }
}
