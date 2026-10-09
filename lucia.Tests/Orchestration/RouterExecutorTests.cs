using System.Text.Json;

using A2A;

using FakeItEasy;

using lucia.Agents;
using lucia.Agents.Orchestration;
using lucia.Agents.Registry;
using lucia.Tests.TestDoubles;

using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace lucia.Tests.Orchestration;

/// <summary>
/// Tests for preserving original user text in router results.
/// </summary>
public sealed class RouterExecutorTests
{
    [Fact]
    public async Task HandleAsync_PopulatesOriginalUserTextFromMessageMetadata()
    {
        // Arrange
        var responsePayload = JsonSerializer.Serialize(new
        {
            agentId = "light-agent",
            reasoning = "Light request",
            confidence = 0.9
        });

        var chatClient = new StubChatClient(
        [
            _ => new ChatResponse([new ChatMessage(ChatRole.Assistant, responsePayload)])
        ]);

        var registry = A.Fake<IAgentRegistry>();
        var agent = new AgentCard
        {
            Name = "light-agent",
            Description = "Controls lights",
            Version = "1.0.0",
            Capabilities = new AgentCapabilities(),
            DefaultInputModes = ["text"],
            DefaultOutputModes = ["text"],
            Skills = []
        };

        A.CallTo(() => registry.GetEnumerableAgentsAsync(A<CancellationToken>._))
            .Returns(GetAgentsAsync(agent));

        var router = new RouterExecutor(
            chatClient,
            registry,
            A.Fake<Microsoft.Extensions.Logging.ILogger<RouterExecutor>>(),
            new AgentsTelemetrySource(),
            Options.Create(new RouterExecutorOptions()));

        var message = new ChatMessage(ChatRole.User, "HOME ASSISTANT CONTEXT\n\nUser: turn on the basement")
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                ["lucia.originalUserText"] = "schalte den keller ein"
            }
        };

        // Act
        var result = await router.HandleAsync(message, A.Fake<IWorkflowContext>(), CancellationToken.None);

        // Assert
        Assert.Equal("schalte den keller ein", result.OriginalUserText);
    }

    [Fact]
    public async Task HandleAsync_AcceptsSlugifiedAgentId_WhenCardNameIsDisplayText()
    {
        // Legacy/built-in cards may carry display-text names. The router LLM routinely
        // emits the kebab-case slug instead — that must still validate as a known agent.
        var responsePayload = JsonSerializer.Serialize(new
        {
            agentId = "open-wearables-agent",
            reasoning = "Wearable activity request",
            confidence = 0.95
        });

        var chatClient = new StubChatClient(
        [
            _ => new ChatResponse([new ChatMessage(ChatRole.Assistant, responsePayload)])
        ]);

        var registry = A.Fake<IAgentRegistry>();
        var agent = new AgentCard
        {
            Name = "Open Wearables Agent",
            Description = "Tracks wearable activity",
            Version = "1.0.0",
            Capabilities = new AgentCapabilities(),
            DefaultInputModes = ["text"],
            DefaultOutputModes = ["text"],
            Skills = []
        };

        A.CallTo(() => registry.GetEnumerableAgentsAsync(A<CancellationToken>._))
            .Returns(GetAgentsAsync(agent));

        var router = new RouterExecutor(
            chatClient,
            registry,
            A.Fake<Microsoft.Extensions.Logging.ILogger<RouterExecutor>>(),
            new AgentsTelemetrySource(),
            Options.Create(new RouterExecutorOptions()));

        var result = await router.HandleAsync(
            new ChatMessage(ChatRole.User, "How many steps did I take yesterday?"),
            A.Fake<IWorkflowContext>(),
            CancellationToken.None);

        Assert.Equal("open-wearables-agent", result.AgentId);
    }

    private static async IAsyncEnumerable<AgentCard> GetAgentsAsync(params AgentCard[] agents)
    {
        foreach (var agent in agents)
        {
            yield return agent;
            await Task.Yield();
        }
    }
}
