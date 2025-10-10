using System.Collections.Concurrent;
using System.Net.Http;
using System.Linq;
using System.Text.Json;
using A2A;
using lucia.Agents.Orchestration;
using lucia.Agents.Registry;
using lucia.Tests.TestDoubles;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.CompilerServices;

namespace lucia.Tests;

public sealed class LuciaOrchestratorTests
{
    [Fact]
    public async Task ProcessRequestAsync_RoutesToSelectedAgentAndReturnsAggregatedResponse()
    {
        // Arrange
        const string userRequest = "Turn on the hallway lights.";
        const string expectedAgentId = "light-agent";
        const string agentResponseText = "I've turned on the hallway lights.";

        var routerChoice = new AgentChoiceResult
        {
            AgentId = expectedAgentId,
            Confidence = 0.92,
            Reasoning = "Lighting request matched light-agent",
            AdditionalAgents = null
        };

        var chatClient = new StubChatClient([
            _ => CreateRouterResponse(routerChoice)
        ]);

        var agentCards = new[]
        {
            CreateAgentCard("light-agent", "/agents/light", "Controls lighting"),
            CreateAgentCard("general-assistant", "/agents/general", "Fallback assistant")
        };

        var registry = new StaticAgentRegistry(agentCards);

        var lightAgent = new StubAIAgent(
            runAsync: (_, _, _) => Task.FromResult(new AgentRunResponse(new ChatMessage(ChatRole.Assistant, agentResponseText))),
            id: expectedAgentId,
            name: expectedAgentId);

        var generalAgent = new StubAIAgent(
            runAsync: (_, _, _) => Task.FromResult(new AgentRunResponse(new ChatMessage(ChatRole.Assistant, "Fallback"))),
            id: "general-assistant",
            name: "general-assistant");

        var agentCatalog = new StubAgentCatalog([lightAgent, generalAgent]);

        var orchestrator = CreateOrchestrator(
            chatClient,
            registry,
            agentCatalog,
            NullLoggerFactory.Instance,
            Options.Create(new RouterExecutorOptions { ConfidenceThreshold = 0.5 }),
            Options.Create(new AgentExecutorWrapperOptions { Timeout = TimeSpan.FromSeconds(5) }),
            Options.Create(new ResultAggregatorOptions()),
            TimeProvider.System);

        // Act
        var response = await orchestrator.ProcessRequestAsync(userRequest, CancellationToken.None);

        // Assert
        Assert.Equal(agentResponseText, response);
        Assert.Equal(1, chatClient.InvocationCount);
        var capturedHistory = Assert.Single(chatClient.CapturedMessages);
        var capturedUserMessage = Assert.Single(capturedHistory, message => message.Role == ChatRole.User);
        Assert.Equal(ChatRole.User, capturedUserMessage.Role);
        Assert.Contains(userRequest, ExtractContent(capturedUserMessage), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProcessRequestAsync_AgentFailureStillAggregatesAndReturnsMessage()
    {
        // Arrange
        const string failingAgentId = "music-agent";
        const string fallbackMessagePrefix = "However, I couldn't complete";

        var routerChoice = new AgentChoiceResult
        {
            AgentId = failingAgentId,
            Confidence = 0.95,
            Reasoning = "Music request matched music-agent",
            AdditionalAgents = null
        };

        var chatClient = new StubChatClient([
            _ => CreateRouterResponse(routerChoice)
        ]);

        var agentCards = new[]
        {
            CreateAgentCard("music-agent", "/agents/music", "Controls music"),
            CreateAgentCard("general-assistant", "/agents/general", "Fallback assistant")
        };

        var registry = new StaticAgentRegistry(agentCards);

        var musicAgent = new StubAIAgent(
            runAsync: (_, _, _) => throw new InvalidOperationException("Player offline"),
            id: failingAgentId,
            name: failingAgentId);

        var generalAgent = new StubAIAgent(
            runAsync: (_, _, _) => Task.FromResult(new AgentRunResponse(new ChatMessage(ChatRole.Assistant, "General assistance ready."))),
            id: "general-assistant",
            name: "general-assistant");

        var agentCatalog = new StubAgentCatalog([musicAgent, generalAgent]);

        var orchestrator = CreateOrchestrator(
            chatClient,
            registry,
            agentCatalog,
            NullLoggerFactory.Instance,
            Options.Create(new RouterExecutorOptions { ConfidenceThreshold = 0.5 }),
            Options.Create(new AgentExecutorWrapperOptions { Timeout = TimeSpan.FromMilliseconds(50) }),
            Options.Create(new ResultAggregatorOptions()),
            TimeProvider.System);

        // Act
        var response = await orchestrator.ProcessRequestAsync("Play some jazz", CancellationToken.None);

        // Assert
        Assert.Contains(fallbackMessagePrefix, response, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Player offline", response, StringComparison.OrdinalIgnoreCase);
    }

    private static LuciaOrchestrator CreateOrchestrator(
        IChatClient chatClient,
        AgentRegistry registry,
        AgentCatalog agentCatalog,
        ILoggerFactory loggerFactory,
        IOptions<RouterExecutorOptions> routerOptions,
        IOptions<AgentExecutorWrapperOptions> wrapperOptions,
        IOptions<ResultAggregatorOptions> aggregatorOptions,
        TimeProvider timeProvider)
    {
        var services = new ServiceCollection();
        services.AddSingleton(loggerFactory);
        services.AddSingleton(chatClient);
        services.AddSingleton(registry);
        services.AddSingleton(agentCatalog);
        services.AddSingleton(routerOptions);
        services.AddSingleton(wrapperOptions);
        services.AddSingleton(aggregatorOptions);
        services.AddSingleton(timeProvider);
        services.AddSingleton<IHttpClientFactory>(new StubHttpClientFactory());
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        var provider = services.BuildServiceProvider();

        return new LuciaOrchestrator(
            chatClient,
            registry,
            agentCatalog,
            provider,
            provider.GetRequiredService<IHttpClientFactory>(),
            provider.GetRequiredService<ILogger<LuciaOrchestrator>>(),
            loggerFactory,
            routerOptions,
            wrapperOptions,
            aggregatorOptions,
            timeProvider);
    }

    private static ChatResponse CreateRouterResponse(AgentChoiceResult choice)
    {
        var payload = JsonSerializer.Serialize(choice, RouterExecutor.JsonSerializerOptions);
        return new ChatResponse([
            new ChatMessage(ChatRole.Assistant, payload)
        ]);
    }

    private static AgentCard CreateAgentCard(string name, string url, string description)
    {
        return new AgentCard
        {
            Name = name,
            Url = url,
            Description = description,
            Capabilities = new AgentCapabilities
            {
                PushNotifications = true,
                Streaming = true,
                StateTransitionHistory = true
            },
            DefaultInputModes = ["text"],
            DefaultOutputModes = ["text"],
            Version = "1.0.0"
        };
    }

    private static string ExtractContent(ChatMessage message)
    {
        if (message.Contents is { Count: > 0 })
        {
            return string.Join(' ', message.Contents.OfType<TextContent>().Select(t => t.Text));
        }

        return message.Text ?? string.Empty;
    }

}
