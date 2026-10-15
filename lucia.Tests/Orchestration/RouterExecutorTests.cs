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
    public async Task HandleAsync_SlugifiedAgentId_ResolvesToCanonicalCardName()
    {
        // Legacy/built-in cards may carry display-text names. The router LLM routinely
        // emits the kebab-case slug instead — it must resolve to the exact card name,
        // because that is what agent dispatch keys on.
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

        Assert.Equal("Open Wearables Agent", result.AgentId);

        // Dispatch seam: WorkflowFactory keys local agent invokers by the card name, so
        // the canonicalized id must hit an invoker dictionary built that way.
        var invokers = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            [agent.Name] = new object()
        };
        Assert.True(invokers.ContainsKey(result.AgentId));
    }

    [Fact]
    public async Task HandleAsync_CaseInsensitiveExactMatch_CanonicalizesCardName()
    {
        var responsePayload = JsonSerializer.Serialize(new
        {
            agentId = "LIGHT-AGENT",
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

        var result = await router.HandleAsync(
            new ChatMessage(ChatRole.User, "Turn on the lights"),
            A.Fake<IWorkflowContext>(),
            CancellationToken.None);

        Assert.Equal("light-agent", result.AgentId);
    }

    [Fact]
    public async Task HandleAsync_UnknownAgentId_FallsBack()
    {
        var responsePayload = JsonSerializer.Serialize(new
        {
            agentId = "nonexistent-agent",
            reasoning = "Unknown request",
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

        var result = await router.HandleAsync(
            new ChatMessage(ChatRole.User, "Do something unknown"),
            A.Fake<IWorkflowContext>(),
            CancellationToken.None);

        Assert.Equal(RouterExecutorOptions.DefaultFallbackAgentId, result.AgentId);
        Assert.Equal(0, result.Confidence);
    }

    [Fact]
    public async Task HandleAsync_AmbiguousSlug_FallsBack()
    {
        // Two cards whose names slugify to the same value make a slug-level match
        // ambiguous — routing by slug alone could hit the wrong agent.
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
        var first = CreateCard("Open Wearables Agent", "Tracks wearable activity");
        var second = CreateCard("Open.Wearables.Agent", "Tracks wearable activity (second card)");

        A.CallTo(() => registry.GetEnumerableAgentsAsync(A<CancellationToken>._))
            .Returns(GetAgentsAsync(first, second));

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

        Assert.Equal(RouterExecutorOptions.DefaultFallbackAgentId, result.AgentId);
    }

    private static AgentCard CreateCard(string name, string description) => new()
    {
        Name = name,
        Description = description,
        Version = "1.0.0",
        Capabilities = new AgentCapabilities(),
        DefaultInputModes = ["text"],
        DefaultOutputModes = ["text"],
        Skills = []
    };

    private static async IAsyncEnumerable<AgentCard> GetAgentsAsync(params AgentCard[] agents)
    {
        foreach (var agent in agents)
        {
            yield return agent;
            await Task.Yield();
        }
    }
}
