using System.Runtime.CompilerServices;
using System.Text.Json;

using A2A;

using FakeItEasy;

using lucia.Agents;
using lucia.Agents.Orchestration;
using lucia.Agents.Registry;
using lucia.Tests.TestDoubles;

using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace lucia.Tests.Orchestration;

/// <summary>
/// Provider-free unit tests for <see cref="RouterExecutor"/> routing and fallback logic.
/// Uses <see cref="StubChatClient"/> with pre-serialized JSON so CI exercises the real
/// routing/fallback/clarification/normalization code paths without a live LLM.
/// </summary>
public sealed class RouterExecutorFallbackTests
{
    // ─── Helpers ─────────────────────────────────────────────────────

    private static RouterExecutor CreateRouter(
        IChatClient chatClient,
        IAgentRegistry registry,
        RouterExecutorOptions? options = null)
    {
        return new RouterExecutor(
            chatClient,
            registry,
            NullLogger<RouterExecutor>.Instance,
            new AgentsTelemetrySource(),
            Options.Create(options ?? new RouterExecutorOptions()));
    }

    private static AgentCard MakeAgent(string name, string description = "Test agent") => new()
    {
        Name = name,
        Description = description,
        Version = "1.0.0",
        Capabilities = new AgentCapabilities(),
        DefaultInputModes = ["text"],
        DefaultOutputModes = ["text"],
        Skills = []
    };

    private static IAgentRegistry RegistryWith(params AgentCard[] agents)
    {
        var registry = A.Fake<IAgentRegistry>();
        A.CallTo(() => registry.GetEnumerableAgentsAsync(A<CancellationToken>._))
            .Returns(StreamAgentsAsync(agents));
        return registry;
    }

    private static async IAsyncEnumerable<AgentCard> StreamAgentsAsync(
        IEnumerable<AgentCard> agents,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var agent in agents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return agent;
            await Task.Yield();
        }
    }

    private static string RoutingJson(
        string agentId,
        double confidence = 0.95,
        string? reasoning = null,
        string[]? additionalAgents = null)
    {
        return JsonSerializer.Serialize(new
        {
            agentId,
            reasoning = reasoning ?? $"Route to {agentId}.",
            confidence,
            additionalAgents
        }, RouterExecutor.JsonSerializerOptions);
    }

    // ─── No agents registered ─────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_NoAgentsRegistered_ReturnsFallbackWithoutCallingLlm()
    {
        var chatClient = A.Fake<IChatClient>();
        var router = CreateRouter(chatClient, RegistryWith());

        var result = await router.HandleAsync(
            new ChatMessage(ChatRole.User, "turn on the lights"),
            A.Fake<IWorkflowContext>());

        Assert.Equal(RouterExecutorOptions.DefaultFallbackAgentId, result.AgentId);
        Assert.Equal(0, result.Confidence);
        A.CallTo(() => chatClient.GetResponseAsync(
            A<IEnumerable<ChatMessage>>._, A<ChatOptions>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    // ─── LLM returns unknown agent → fallback ─────────────────────────

    [Fact]
    public async Task HandleAsync_LlmReturnsUnknownAgent_ReturnsFallback()
    {
        var registry = RegistryWith(MakeAgent("light-agent"));
        var chatClient = new StubChatClient([
            _ => new ChatResponse([new ChatMessage(ChatRole.Assistant,
                RoutingJson("nonexistent-agent"))])
        ]);

        var router = CreateRouter(chatClient, registry);
        var result = await router.HandleAsync(
            new ChatMessage(ChatRole.User, "do something"),
            A.Fake<IWorkflowContext>());

        Assert.Equal(RouterExecutorOptions.DefaultFallbackAgentId, result.AgentId);
        Assert.Equal(0, result.Confidence);
    }

    // ─── Confidence below threshold → clarification ──────────────────

    [Fact]
    public async Task HandleAsync_ConfidenceBelowThreshold_ReturnsClarificationAgent()
    {
        var registry = RegistryWith(
            MakeAgent("light-agent"),
            MakeAgent("climate-agent"));

        // Confidence 0.5 < default threshold 0.7 → clarification
        var chatClient = new StubChatClient([
            _ => new ChatResponse([new ChatMessage(ChatRole.Assistant,
                RoutingJson("light-agent", confidence: 0.5,
                    reasoning: "Unclear device. Which one did you mean?"))])
        ]);

        var router = CreateRouter(chatClient, registry);
        var result = await router.HandleAsync(
            new ChatMessage(ChatRole.User, "adjust the thing"),
            A.Fake<IWorkflowContext>());

        Assert.Equal(RouterExecutorOptions.DefaultClarificationAgentId, result.AgentId);
    }

    // ─── All JSON parse attempts exhaust → fallback ───────────────────

    [Fact]
    public async Task HandleAsync_AllAttemptsMalformedJson_ReturnsFallbackAfterRetries()
    {
        var registry = RegistryWith(MakeAgent("light-agent"));
        const string malformedJson = "not json at all";
        var chatClient = new StubChatClient([
            _ => new ChatResponse([new ChatMessage(ChatRole.Assistant, malformedJson)]),
            _ => new ChatResponse([new ChatMessage(ChatRole.Assistant, malformedJson)])
        ]);

        var options = new RouterExecutorOptions { MaxAttempts = 2 };
        var router = CreateRouter(chatClient, registry, options);
        var result = await router.HandleAsync(
            new ChatMessage(ChatRole.User, "turn on lights"),
            A.Fake<IWorkflowContext>());

        Assert.Equal(RouterExecutorOptions.DefaultFallbackAgentId, result.AgentId);
        Assert.Equal(0, result.Confidence);
        Assert.Equal(2, chatClient.InvocationCount);
    }

    // ─── NormalizeAdditionalAgents: unknown names filtered ───────────

    [Fact]
    public async Task HandleAsync_AdditionalAgentsContainUnknownNames_FiltersToKnownOnly()
    {
        var registry = RegistryWith(
            MakeAgent("light-agent"),
            MakeAgent("music-agent"));

        var payload = RoutingJson("light-agent", additionalAgents: ["music-agent", "nonexistent-agent", "fantasy-agent"]);
        var chatClient = new StubChatClient([
            _ => new ChatResponse([new ChatMessage(ChatRole.Assistant, payload)])
        ]);

        var router = CreateRouter(chatClient, registry);
        var result = await router.HandleAsync(
            new ChatMessage(ChatRole.User, "dim lights and play music"),
            A.Fake<IWorkflowContext>());

        Assert.Equal("light-agent", result.AgentId);
        Assert.NotNull(result.AdditionalAgents);
        Assert.Single(result.AdditionalAgents);
        Assert.Equal("music-agent", result.AdditionalAgents[0]);
    }

    // ─── NormalizeAdditionalAgents: primary agent removed from list ──

    [Fact]
    public async Task HandleAsync_AdditionalAgentsContainPrimaryAgent_RemovesPrimary()
    {
        var registry = RegistryWith(
            MakeAgent("light-agent"),
            MakeAgent("music-agent"));

        // LLM mistakenly includes the primary in additionalAgents
        var payload = RoutingJson("light-agent", additionalAgents: ["light-agent", "music-agent"]);
        var chatClient = new StubChatClient([
            _ => new ChatResponse([new ChatMessage(ChatRole.Assistant, payload)])
        ]);

        var router = CreateRouter(chatClient, registry);
        var result = await router.HandleAsync(
            new ChatMessage(ChatRole.User, "control lights and music"),
            A.Fake<IWorkflowContext>());

        Assert.Equal("light-agent", result.AgentId);
        Assert.NotNull(result.AdditionalAgents);
        Assert.Single(result.AdditionalAgents);
        Assert.DoesNotContain("light-agent", result.AdditionalAgents, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("music-agent", result.AdditionalAgents[0]);
    }

    // ─── OriginalUserText preserved on no-agents fallback ────────────

    [Fact]
    public async Task HandleAsync_OriginalUserTextInMetadata_PreservedOnFallback()
    {
        var router = CreateRouter(A.Fake<IChatClient>(), RegistryWith());
        var message = new ChatMessage(ChatRole.User, "Licht an")
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                ["lucia.originalUserText"] = "Licht an"
            }
        };

        var result = await router.HandleAsync(message, A.Fake<IWorkflowContext>());

        Assert.Equal("Licht an", result.OriginalUserText);
    }
}
