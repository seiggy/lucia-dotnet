using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AgentCapabilities = A2A.AgentCapabilities;
using AgentCard = A2A.AgentCard;
using lucia.Agents.Orchestration;
using lucia.Tests.TestDoubles;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace lucia.Tests;

public class RouterExecutorTests
{
    private static readonly AgentCard LightAgent = CreateAgent(
        name: "light-agent",
        description: "Controls lighting scenes and brightness.");

    private static readonly AgentCard MusicAgent = CreateAgent(
        name: "music-agent",
        description: "Controls music playback and speakers.");

    private static readonly AgentCard ClimateAgent = CreateAgent(
        name: "climate-agent",
        description: "Adjusts climate and thermostat settings.");

    [Fact]
    public async Task HandleAsync_SelectsLightingAgent_WhenModelRespondsConfidently()
    {
        var message = new ChatMessage(ChatRole.User, "Turn on the kitchen lights");
        var chatClient = new StubChatClient([
            _ => CreateJsonResponse(new AgentChoiceResult
            {
                AgentId = LightAgent.Name,
                Reasoning = "Lighting command detected",
                Confidence = 0.92,
            })
        ]);

        var executor = CreateExecutor(chatClient);

        var result = await executor.HandleAsync(message, new RecordingWorkflowContext());

        Assert.Equal(LightAgent.Name, result.AgentId);
        Assert.True(result.Confidence > 0.9);
        Assert.Contains("Lighting", result.Reasoning, StringComparison.OrdinalIgnoreCase);

        var captured = Assert.Single(chatClient.CapturedMessages);
        Assert.Contains("light-agent", ExtractContent(captured.Last()), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HandleAsync_SelectsMusicAgent_WhenModelRespondsConfidently()
    {
        var message = new ChatMessage(ChatRole.User, "Play some jazz in the living room");
        var chatClient = new StubChatClient([
            _ => CreateJsonResponse(new AgentChoiceResult
            {
                AgentId = MusicAgent.Name,
                Reasoning = "Music playback request",
                Confidence = 0.88,
            })
        ]);

        var executor = CreateExecutor(chatClient);

        var result = await executor.HandleAsync(message, new RecordingWorkflowContext());

        Assert.Equal(MusicAgent.Name, result.AgentId);
        Assert.InRange(result.Confidence, 0.8, 1.0);
    }

    [Fact]
    public async Task HandleAsync_ReturnsClarification_WhenConfidenceBelowThreshold()
    {
        var message = new ChatMessage(ChatRole.User, "Make it warmer");
        var response = new AgentChoiceResult
        {
            AgentId = ClimateAgent.Name,
            Reasoning = "Could be lighting or climate",
            Confidence = 0.42,
        };

        var chatClient = new StubChatClient([_ => CreateJsonResponse(response)]);

        var options = Options.Create(new RouterExecutorOptions
        {
            ConfidenceThreshold = 0.7,
            ClarificationAgentId = "clarification",
            ClarificationPromptTemplate = "Not sure which agent applies. Candidates: {0}."
        });

        var executor = CreateExecutor(chatClient, options);

        var result = await executor.HandleAsync(message, new RecordingWorkflowContext());

        Assert.Equal(options.Value.ClarificationAgentId, result.AgentId);
        Assert.Equal(response.Confidence, result.Confidence, 3);
        Assert.Contains("Candidates", result.Reasoning, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(LightAgent.Name, result.Reasoning, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(ClimateAgent.Name, result.Reasoning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HandleAsync_Retries_OnMalformedJson()
    {
        var message = new ChatMessage(ChatRole.User, "Turn on the hallway lights");
        var chatClient = new StubChatClient([
            _ => CreateRawResponse("{not-valid-json"),
            _ => CreateJsonResponse(new AgentChoiceResult
            {
                AgentId = LightAgent.Name,
                Reasoning = "Lighting command",
                Confidence = 0.91,
            })
        ]);

        var executor = CreateExecutor(chatClient, Options.Create(new RouterExecutorOptions
        {
            MaxAttempts = 2
        }));

        var result = await executor.HandleAsync(message, new RecordingWorkflowContext());

        Assert.Equal(LightAgent.Name, result.AgentId);
        Assert.Equal(2, chatClient.InvocationCount);
    }

    [Fact]
    public async Task HandleAsync_FallsBack_WhenRetriesExhausted()
    {
        var message = new ChatMessage(ChatRole.User, "What's the status of the garden sensors?");
        var chatClient = new StubChatClient([
            _ => CreateRawResponse("not-json"),
            _ => CreateRawResponse("still-not-json")
        ]);

        var options = Options.Create(new RouterExecutorOptions
        {
            MaxAttempts = 2,
            FallbackAgentId = "general-assistant",
            FallbackReasonTemplate = "Falling back after failure: {0}"
        });

        var executor = CreateExecutor(chatClient, options);

        var result = await executor.HandleAsync(message, new RecordingWorkflowContext());

    Assert.Equal(options.Value.FallbackAgentId, result.AgentId);
    Assert.Equal(0, result.Confidence);
    Assert.Contains("Falling back", result.Reasoning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HandleAsync_UsesFallback_WhenAgentNotRegistered()
    {
        var message = new ChatMessage(ChatRole.User, "Tell me a joke");
        var chatClient = new StubChatClient([
            _ => CreateJsonResponse(new AgentChoiceResult
            {
                AgentId = "humor-agent",
                Reasoning = "Detected humor request",
                Confidence = 0.95,
            })
        ]);

        var executor = CreateExecutor(chatClient);

        var result = await executor.HandleAsync(message, new RecordingWorkflowContext());

        Assert.Equal(RouterExecutorOptions.DefaultFallbackAgentId, result.AgentId);
        Assert.Equal(0, result.Confidence);
        Assert.Contains("humor-agent", result.Reasoning, StringComparison.OrdinalIgnoreCase);
    }

    private static RouterExecutor CreateExecutor(StubChatClient chatClient, IOptions<RouterExecutorOptions>? options = null)
    {
        var registry = new StaticAgentRegistry([LightAgent, MusicAgent, ClimateAgent]);
        var executorOptions = options ?? Options.Create(new RouterExecutorOptions());
        return new RouterExecutor(chatClient, registry, NullLogger<RouterExecutor>.Instance, executorOptions);
    }

    private static AgentCard CreateAgent(string name, string description) => new()
    {
        Url = $"/a2a/{name}",
        Name = name,
        Description = description,
        Capabilities = new AgentCapabilities
        {
            PushNotifications = true,
            StateTransitionHistory = true,
            Streaming = true
        },
        DefaultInputModes = ["text"],
        DefaultOutputModes = ["text"],
        Version = "1.0.0",
    };

    private static ChatResponse CreateJsonResponse(AgentChoiceResult result)
    {
        var payload = JsonSerializer.Serialize(result, RouterExecutor.JsonSerializerOptions);
        return CreateRawResponse(payload);
    }

    private static ChatResponse CreateRawResponse(string payload)
    {
        return new ChatResponse([
            new ChatMessage(ChatRole.Assistant, payload)
        ]);
    }

    private static string ExtractContent(ChatMessage message)
    {
        return message switch
        {
            { Contents: { } contents } when contents.Count > 0 => string.Join(' ', contents.OfType<TextContent>().Select(t => t.Text)),
            _ => string.Join(' ', message?.Text is string text ? new[] { text } : []),
        };
    }
}
