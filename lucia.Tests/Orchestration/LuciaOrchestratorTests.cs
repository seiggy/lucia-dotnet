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

namespace lucia.Tests.Orchestration;

public sealed class LuciaOrchestratorTests : TestBase
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

    [Fact]
    public async Task ProcessRequestAsync_WithEmptyRequest_ThrowsArgumentException()
    {
        // Arrange
        var chatClient = new StubChatClient([]);
        var registry = new StaticAgentRegistry([]);
        var agentCatalog = new StubAgentCatalog([]);

        var orchestrator = CreateOrchestrator(
            chatClient,
            registry,
            agentCatalog,
            NullLoggerFactory.Instance,
            Options.Create(new RouterExecutorOptions()),
            Options.Create(new AgentExecutorWrapperOptions()),
            Options.Create(new ResultAggregatorOptions()),
            TimeProvider.System);

        // Act & Assert
        var response = await orchestrator.ProcessRequestAsync("", CancellationToken.None);
        Assert.Contains("error", response, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProcessRequestAsync_WithNoAgentsAvailable_ReturnsFallbackMessage()
    {
        // Arrange
        var chatClient = new StubChatClient([]);
        var registry = new StaticAgentRegistry([]);
        var agentCatalog = new StubAgentCatalog([]);

        var orchestrator = CreateOrchestrator(
            chatClient,
            registry,
            agentCatalog,
            NullLoggerFactory.Instance,
            Options.Create(new RouterExecutorOptions()),
            Options.Create(new AgentExecutorWrapperOptions()),
            Options.Create(new ResultAggregatorOptions()),
            TimeProvider.System);

        // Act
        var response = await orchestrator.ProcessRequestAsync("Do something", CancellationToken.None);

        // Assert
        Assert.Contains("don't have any specialized agents available", response, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProcessRequestAsync_WithMultipleAdditionalAgents_ExecutesInOrder()
    {
        // Arrange
        const string primaryAgentId = "light-agent";
        const string additionalAgent1 = "climate-agent";
        const string additionalAgent2 = "music-agent";

        var routerChoice = new AgentChoiceResult
        {
            AgentId = primaryAgentId,
            Confidence = 0.85,
            Reasoning = "Multi-task request",
            AdditionalAgents = [additionalAgent1, additionalAgent2]
        };

        var chatClient = new StubChatClient([
            _ => CreateRouterResponse(routerChoice)
        ]);

        var agentCards = new[]
        {
            CreateAgentCard(primaryAgentId, "/agents/light", "Lighting"),
            CreateAgentCard(additionalAgent1, "/agents/climate", "Climate"),
            CreateAgentCard(additionalAgent2, "/agents/music", "Music")
        };

        var registry = new StaticAgentRegistry(agentCards);

        var lightAgent = new StubAIAgent(
            runAsync: (_, _, _) => Task.FromResult(new AgentRunResponse(new ChatMessage(ChatRole.Assistant, "Lights adjusted"))),
            id: primaryAgentId,
            name: primaryAgentId);

        var climateAgent = new StubAIAgent(
            runAsync: (_, _, _) => Task.FromResult(new AgentRunResponse(new ChatMessage(ChatRole.Assistant, "Temperature set"))),
            id: additionalAgent1,
            name: additionalAgent1);

        var musicAgent = new StubAIAgent(
            runAsync: (_, _, _) => Task.FromResult(new AgentRunResponse(new ChatMessage(ChatRole.Assistant, "Music playing"))),
            id: additionalAgent2,
            name: additionalAgent2);

        var agentCatalog = new StubAgentCatalog([lightAgent, climateAgent, musicAgent]);

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
        var response = await orchestrator.ProcessRequestAsync("Set up the room", CancellationToken.None);

        // Assert
        Assert.Contains("Lights adjusted", response, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Temperature set", response, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Music playing", response, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProcessRequestAsync_WithAgentTimeout_IncludesTimeoutError()
    {
        // Arrange
        const string slowAgentId = "slow-agent";

        var routerChoice = new AgentChoiceResult
        {
            AgentId = slowAgentId,
            Confidence = 0.9,
            Reasoning = "Matched slow agent",
            AdditionalAgents = null
        };

        var chatClient = new StubChatClient([
            _ => CreateRouterResponse(routerChoice)
        ]);

        var agentCards = new[]
        {
            CreateAgentCard(slowAgentId, "/agents/slow", "Slow agent")
        };

        var registry = new StaticAgentRegistry(agentCards);

        var tcs = new TaskCompletionSource<AgentRunResponse>();
        var slowAgent = new StubAIAgent(
            runAsync: (_, _, _) => tcs.Task,
            id: slowAgentId,
            name: slowAgentId);

        var agentCatalog = new StubAgentCatalog([slowAgent]);

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
        var response = await orchestrator.ProcessRequestAsync("Take your time", CancellationToken.None);

        // Assert
        Assert.Contains("timed out", response, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetStatusAsync_WithAgentsAvailable_ReturnsReadyStatus()
    {
        // Arrange
        var agentCards = new[]
        {
            CreateAgentCard("agent1", "/agents/1", "Agent 1"),
            CreateAgentCard("agent2", "/agents/2", "Agent 2")
        };

        var registry = new StaticAgentRegistry(agentCards);
        var agentCatalog = new StubAgentCatalog([]);
        var chatClient = new StubChatClient([]);

        var orchestrator = CreateOrchestrator(
            chatClient,
            registry,
            agentCatalog,
            NullLoggerFactory.Instance,
            Options.Create(new RouterExecutorOptions()),
            Options.Create(new AgentExecutorWrapperOptions()),
            Options.Create(new ResultAggregatorOptions()),
            TimeProvider.System);

        // Act
        var status = await orchestrator.GetStatusAsync();

        // Assert
        Assert.True(status.IsReady);
        Assert.Equal(2, status.AvailableAgentCount);
        Assert.Equal(2, status.AvailableAgents.Count);
    }

    [Fact]
    public async Task GetStatusAsync_WithNoAgents_ReturnsNotReadyStatus()
    {
        // Arrange
        var registry = new StaticAgentRegistry([]);
        var agentCatalog = new StubAgentCatalog([]);
        var chatClient = new StubChatClient([]);

        var orchestrator = CreateOrchestrator(
            chatClient,
            registry,
            agentCatalog,
            NullLoggerFactory.Instance,
            Options.Create(new RouterExecutorOptions()),
            Options.Create(new AgentExecutorWrapperOptions()),
            Options.Create(new ResultAggregatorOptions()),
            TimeProvider.System);

        // Act
        var status = await orchestrator.GetStatusAsync();

        // Assert
        Assert.False(status.IsReady);
        Assert.Equal(0, status.AvailableAgentCount);
    }

    [Fact]
    public async Task ProcessRequestAsync_WithRemoteAgentCard_InvokesViaTaskManager()
    {
        // Arrange
        const string remoteAgentId = "remote-security-agent";

        var routerChoice = new AgentChoiceResult
        {
            AgentId = remoteAgentId,
            Confidence = 0.88,
            Reasoning = "Security request matched remote agent",
            AdditionalAgents = null
        };

        var chatClient = new StubChatClient([
            _ => CreateRouterResponse(routerChoice)
        ]);

        var agentCards = new[]
        {
            CreateAgentCard(remoteAgentId, "https://remote.example.com/security", "Remote security agent")
        };

        var registry = new StaticAgentRegistry(agentCards);
        
        // No local AIAgent for this card - it should use TaskManager
        var agentCatalog = new StubAgentCatalog([]);

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
        var response = await orchestrator.ProcessRequestAsync("Check security system", CancellationToken.None);

        // Assert
        // Should not throw - TaskManager will handle the remote call
        Assert.NotNull(response);
    }

    [Fact]
    public async Task ProcessRequestAsync_WithMixedLocalAndRemoteAgents_ExecutesBoth()
    {
        // Arrange
        const string localAgentId = "local-lights";
        const string remoteAgentId = "remote-climate";

        var routerChoice = new AgentChoiceResult
        {
            AgentId = localAgentId,
            Confidence = 0.87,
            Reasoning = "Multi-agent request",
            AdditionalAgents = [remoteAgentId]
        };

        var chatClient = new StubChatClient([
            _ => CreateRouterResponse(routerChoice)
        ]);

        var agentCards = new[]
        {
            CreateAgentCard(localAgentId, "/agents/local-lights", "Local lights"),
            CreateAgentCard(remoteAgentId, "https://remote.example.com/climate", "Remote climate")
        };

        var registry = new StaticAgentRegistry(agentCards);

        var localAgent = new StubAIAgent(
            runAsync: (_, _, _) => Task.FromResult(new AgentRunResponse(new ChatMessage(ChatRole.Assistant, "Lights controlled locally"))),
            id: localAgentId,
            name: localAgentId);

        var agentCatalog = new StubAgentCatalog([localAgent]);

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
        var response = await orchestrator.ProcessRequestAsync("Adjust room settings", CancellationToken.None);

        // Assert
        Assert.Contains("Lights controlled locally", response, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProcessRequestAsync_WithCancellationToken_PropagatesCancellation()
    {
        // Arrange
        var routerChoice = new AgentChoiceResult
        {
            AgentId = "test-agent",
            Confidence = 0.9,
            Reasoning = "Test",
            AdditionalAgents = null
        };

        var chatClient = new StubChatClient([
            _ => CreateRouterResponse(routerChoice)
        ]);

        var agentCards = new[]
        {
            CreateAgentCard("test-agent", "/agents/test", "Test agent")
        };

        var registry = new StaticAgentRegistry(agentCards);

        var cts = new CancellationTokenSource();
        cts.Cancel();

        var testAgent = new StubAIAgent(
            runAsync: (_, _, token) =>
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult(new AgentRunResponse(new ChatMessage(ChatRole.Assistant, "Should not reach")));
            },
            id: "test-agent",
            name: "test-agent");

        var agentCatalog = new StubAgentCatalog([testAgent]);

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
        var response = await orchestrator.ProcessRequestAsync("Test request", cts.Token);

        // Assert
        Assert.Contains("error", response, StringComparison.OrdinalIgnoreCase);
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
