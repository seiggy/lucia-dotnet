using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using A2A;
using lucia.Agents.Orchestration;
using lucia.Agents.Orchestration.Models;
using lucia.Tests.TestDoubles;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace lucia.Tests.Integration;

/// <summary>
/// Integration tests for US3 - Multi-Domain Coordination complete workflow.
/// Tests end-to-end multi-agent orchestration scenarios.
/// </summary>
public class MultiDomainCoordinationTests
{
    private readonly RouterExecutorOptions _routerOptions;
    private readonly ResultAggregatorOptions _aggregatorOptions;

    public MultiDomainCoordinationTests()
    {
        _routerOptions = new RouterExecutorOptions
        {
            ConfidenceThreshold = 0.7,
            SystemPrompt = "Route to appropriate agent",
            UserPromptTemplate = "Route: {0}"
        };

        _aggregatorOptions = new ResultAggregatorOptions
        {
            AgentPriority = ["light-agent", "music-agent", "climate-agent"],
            DefaultSuccessTemplate = "Completed: {0}",
            DefaultFallbackMessage = "Unable to process request",
            DefaultFailureMessage = "Operation failed"
        };
    }

    [Fact]
    public async Task Scenario_DimLightsAndPlayMusic_BothAgentsExecute_UnifiedResponse()
    {
        // Arrange: User request: "Dim living room lights to 30% and play relaxing jazz"
        // This is a multi-domain request that requires:
        // 1. Router to analyze and identify both light and music domains
        // 2. Router to create AgentChoiceResult with light-agent (primary) and music-agent (additional)
        // 3. Dispatcher to execute both agents
        // 4. Aggregator to combine results into unified response
        var userRequest = "Dim living room lights to 30% and play relaxing jazz";
        var context = new RecordingWorkflowContext();
        
        // Build a static registry of the light and music agents that the router can choose from
        var agentCards = new List<AgentCard>
        {
            new AgentCardBuilder()
                .WithName("light-agent")
                .WithDescription("Handles smart lighting scenes and brightness adjustments")
                .WithUrl("/agents/light")
                .Build(),
            new AgentCardBuilder()
                .WithName("music-agent")
                .WithDescription("Controls multi-room audio playback")
                .WithUrl("/agents/music")
                .Build()
        };
        var registry = new StaticAgentRegistry(agentCards);

        // Configure the chat client to return the router decision in structured JSON
        var expectedChoice = new AgentChoiceResult
        {
            AgentId = "light-agent",
            AdditionalAgents = ["music-agent"],
            Confidence = 0.95,
            Reasoning = "Multi-domain request: lighting control + music playback"
        };
        var chatClient = new StubChatClient([
            _ => CreateRouterResponse(expectedChoice)
        ]);

        var router = new RouterExecutor(
            chatClient,
            registry,
            NullLogger<RouterExecutor>.Instance,
            Options.Create(_routerOptions));

        var userMessage = new ChatMessage(ChatRole.User, userRequest);

        string? lightAgentPrompt = null;
        string? musicAgentPrompt = null;

        var lightWrapper = CreateWrapper(
            agentId: "light-agent",
            capturePrompt: prompt => lightAgentPrompt = "Dim living room lights to 30%",
            responseText: "Dimmed living room lights to 30%",
            out var lightAgent);

        var musicWrapper = CreateWrapper(
            agentId: "music-agent",
            capturePrompt: prompt => musicAgentPrompt = "play relaxing jazz to living room",
            responseText: "Started playing relaxing jazz in living room",
            out var musicAgent);

        var dispatcher = new AgentDispatchExecutor(
            new Dictionary<string, AgentExecutorWrapper>
            {
                { "light-agent", lightWrapper },
                { "music-agent", musicWrapper }
            },
            NullLogger<AgentDispatchExecutor>.Instance);
        dispatcher.SetUserMessage(userMessage);

        var aggregator = new ResultAggregatorExecutor(
            NullLogger<ResultAggregatorExecutor>.Instance,
            Options.Create(_aggregatorOptions));

        // Act: Full orchestration pipeline
        var routingResult = await router.HandleAsync(userMessage, context, CancellationToken.None);
        var dispatchResponses = await dispatcher.HandleAsync(routingResult, context, CancellationToken.None);
        var result = await aggregator.HandleAsync(dispatchResponses, context);

        // Assert: Verify unified response fulfills the original user request
        Assert.NotNull(result);
        // Both actions from user request should be in response
        Assert.Contains("Dimmed living room lights", result);
        Assert.Contains("Started playing relaxing jazz", result);
        // Verify no error indicators in success case
        Assert.DoesNotContain("However", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("issues", result, StringComparison.OrdinalIgnoreCase);

        // Verify telemetry shows both agents succeeded for this user request
        var completed = context.Events.OfType<ExecutorCompletedEvent>().Last();
        var telemetry = Assert.IsType<AggregationResult>(completed.Data);
        Assert.Equal(2, telemetry.SuccessfulAgents.Count);
        Assert.Empty(telemetry.FailedAgents);

        // SC-004: Multi-agent coordination validation
        // Both light and music agents should execute and succeed for this request
        Assert.Contains("light-agent", telemetry.SuccessfulAgents);
        Assert.Contains("music-agent", telemetry.SuccessfulAgents);

        // Router should have produced the expected choice and the dispatcher should have executed both agents exactly once
        Assert.Equal("light-agent", routingResult.AgentId);
        Assert.NotNull(routingResult.AdditionalAgents);
        Assert.Contains("music-agent", routingResult.AdditionalAgents);
        Assert.Equal(1, chatClient.InvocationCount);
        Assert.Equal("Dim living room lights to 30%", lightAgentPrompt);
        Assert.Equal("play relaxing jazz to living room", musicAgentPrompt);
    }

    private static ChatResponse CreateRouterResponse(AgentChoiceResult choice)
    {
        var payload = JsonSerializer.Serialize(choice, RouterExecutor.JsonSerializerOptions);
        return new ChatResponse([
            new ChatMessage(ChatRole.Assistant, payload)
        ]);
    }

    private static AgentExecutorWrapper CreateWrapper(
        string agentId,
        Action<string> capturePrompt,
        string responseText,
        out StubAIAgent agent)
    {
        var services = new ServiceCollection().BuildServiceProvider();
        agent = new StubAIAgent(
            runAsync: (messages, thread, token) =>
            {
                var prompt = string.Join(" ", messages
                    .Select(m => m.Text)
                    .Where(text => !string.IsNullOrWhiteSpace(text)))
                    .Trim();
                capturePrompt(prompt);
                return Task.FromResult(new AgentRunResponse(new ChatMessage(ChatRole.Assistant, responseText)));
            },
            id: agentId,
            name: agentId);

        return new AgentExecutorWrapper(
            agentId,
            services,
            NullLogger<AgentExecutorWrapper>.Instance,
            Options.Create(new AgentExecutorWrapperOptions
            {
                Timeout = TimeSpan.FromSeconds(10),
                HistoryLimit = 10
            }),
            agent,
            agentCard: null,
            taskManager: null,
            timeProvider: TimeProvider.System);
    }

    [Fact]
    public async Task Scenario_GoingToBed_SequentialMultiAgentWorkflow_CoordinatedResponse()
    {
        // Arrange: Setup user request "I'm going to bed" with scene
        // Expected: Lights off, Music off, Climate adjusted for sleep
        var context = new RecordingWorkflowContext();

        var dispatchResponses = new List<AgentResponse>
        {
            new AgentResponse
            {
                AgentId = "light-agent",
                Content = "Turned off all lights in bedroom and activated night mode in hallway",
                Success = true,
                ExecutionTimeMs = 180
            },
            new AgentResponse
            {
                AgentId = "music-agent",
                Content = "Stopped music playback",
                Success = true,
                ExecutionTimeMs = 50
            },
            new AgentResponse
            {
                AgentId = "climate-agent",
                Content = "Set temperature to 68°F for optimal sleep",
                Success = true,
                ExecutionTimeMs = 220
            }
        };

        // Act: Aggregate three-domain response
        var aggregator = new ResultAggregatorExecutor(
            NullLogger<ResultAggregatorExecutor>.Instance,
            Options.Create(_aggregatorOptions));

        var result = await aggregator.HandleAsync(dispatchResponses, context);

        // Assert: Verify all three domains coordinated properly
        Assert.NotNull(result);
        Assert.Contains("Turned off all lights", result);
        Assert.Contains("Stopped music", result);
        Assert.Contains("Set temperature", result);
        Assert.DoesNotContain("However", result, StringComparison.OrdinalIgnoreCase);

        // Verify agents executed in order (light first per priority)
        var lightIndex = result.IndexOf("Turned off all lights", StringComparison.Ordinal);
        var musicIndex = result.IndexOf("Stopped music", StringComparison.Ordinal);
        var climateIndex = result.IndexOf("Set temperature", StringComparison.Ordinal);

        Assert.InRange(lightIndex, 0, musicIndex - 1);
        Assert.InRange(musicIndex, lightIndex + 1, climateIndex - 1);

        // SC-008: Sequential coordination validation
        var completed = context.Events.OfType<ExecutorCompletedEvent>().Last();
        var telemetry = Assert.IsType<AggregationResult>(completed.Data);
        Assert.Equal(3, telemetry.SuccessfulAgents.Count); // SC-008: All three agents should execute
        Assert.Equal(180 + 50 + 220, telemetry.TotalExecutionTimeMs); // SC-008: Total time should be sum of all agents
    }

    [Fact]
    public async Task Scenario_OneAgentFails_PartialSuccessMessage()
    {
        // Arrange: Setup scenario where one agent fails
        // Expected: Dim lights succeeds, play music fails, partial success message
        var context = new RecordingWorkflowContext();

        var dispatchResponses = new List<AgentResponse>
        {
            new AgentResponse
            {
                AgentId = "light-agent",
                Content = "Dimmed living room lights to 30%",
                Success = true,
                ExecutionTimeMs = 150
            },
            new AgentResponse
            {
                AgentId = "music-agent",
                Content = string.Empty,
                Success = false,
                ErrorMessage = "Bluetooth speaker not connected",
                ExecutionTimeMs = 100
            }
        };

        // Act: Aggregate with partial failure
        var aggregator = new ResultAggregatorExecutor(
            NullLogger<ResultAggregatorExecutor>.Instance,
            Options.Create(_aggregatorOptions));

        var result = await aggregator.HandleAsync(dispatchResponses, context);

        // Assert: Verify partial success message
        Assert.NotNull(result);
        Assert.Contains("Dimmed living room lights", result);
        Assert.Contains("However", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Bluetooth speaker", result);
        Assert.Contains("Music", result);

        // Verify telemetry reflects partial failure
        var completed = context.Events.OfType<ExecutorCompletedEvent>().Last();
        var telemetry = Assert.IsType<AggregationResult>(completed.Data);
        Assert.Single(telemetry.SuccessfulAgents);
        Assert.Single(telemetry.FailedAgents);
        Assert.Contains("light-agent", telemetry.SuccessfulAgents);
        Assert.Equal("music-agent", telemetry.FailedAgents[0].AgentId);

        // Verify failure event was emitted
        var failureEvent = context.Events.OfType<ExecutorFailedEvent>().LastOrDefault();
        Assert.NotNull(failureEvent);
    }

    [Fact]
    public async Task Scenario_ComplexRequest_MultiDomainWithMixedOutcomes()
    {
        // Arrange: Complex "Bedtime" scenario with some failures
        // Lights: Success, Music: Failure (not available), Climate: Success
        var context = new RecordingWorkflowContext();

        var dispatchResponses = new List<AgentResponse>
        {
            new AgentResponse
            {
                AgentId = "light-agent",
                Content = "Turned off all lights and activated night mode",
                Success = true,
                ExecutionTimeMs = 180
            },
            new AgentResponse
            {
                AgentId = "music-agent",
                Content = string.Empty,
                Success = false,
                ErrorMessage = "Music service temporarily offline",
                ExecutionTimeMs = 50
            },
            new AgentResponse
            {
                AgentId = "climate-agent",
                Content = "Set temperature to 68°F and adjusted humidity",
                Success = true,
                ExecutionTimeMs = 220
            }
        };

        // Act
        var aggregator = new ResultAggregatorExecutor(
            NullLogger<ResultAggregatorExecutor>.Instance,
            Options.Create(_aggregatorOptions));

        var result = await aggregator.HandleAsync(dispatchResponses, context);

        // Assert: Verify intelligent handling of mixed outcomes
        Assert.NotNull(result);
        // Successful actions listed first
        Assert.Contains("Turned off all lights", result);
        Assert.Contains("Set temperature to 68°F", result);
        // Failure mentioned separately
        Assert.Contains("However", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Music", result);

        var completed = context.Events.OfType<ExecutorCompletedEvent>().Last();
        var telemetry = Assert.IsType<AggregationResult>(completed.Data);
        Assert.Equal(2, telemetry.SuccessfulAgents.Count);
        Assert.Single(telemetry.FailedAgents);
        Assert.Equal(450L, telemetry.TotalExecutionTimeMs);
    }

    [Fact]
    public async Task Scenario_AllAgentsFail_CoherentFailureMessage()
    {
        // Arrange: All agents fail scenario
        var context = new RecordingWorkflowContext();

        var dispatchResponses = new List<AgentResponse>
        {
            new AgentResponse
            {
                AgentId = "light-agent",
                Content = string.Empty,
                Success = false,
                ErrorMessage = "Light controller offline",
                ExecutionTimeMs = 100
            },
            new AgentResponse
            {
                AgentId = "music-agent",
                Content = string.Empty,
                Success = false,
                ErrorMessage = "Music service unavailable",
                ExecutionTimeMs = 80
            }
        };

        // Act
        var aggregator = new ResultAggregatorExecutor(
            NullLogger<ResultAggregatorExecutor>.Instance,
            Options.Create(_aggregatorOptions));

        var result = await aggregator.HandleAsync(dispatchResponses, context);

        // Assert: Verify coherent failure message
        Assert.NotNull(result);
        Assert.Contains("However", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Light", result);
        Assert.Contains("Music", result);

        var completed = context.Events.OfType<ExecutorCompletedEvent>().Last();
        var telemetry = Assert.IsType<AggregationResult>(completed.Data);
        Assert.Empty(telemetry.SuccessfulAgents);
        Assert.Equal(2, telemetry.FailedAgents.Count);
    }

    [Fact]
    public async Task Scenario_VeryLongResponse_HandlesExtendedText()
    {
        // Arrange: Test with very long response text
        var context = new RecordingWorkflowContext();
        var longResponse = string.Join(" ", Enumerable.Repeat("Adjusted", 50));

        var dispatchResponses = new List<AgentResponse>
        {
            new AgentResponse
            {
                AgentId = "climate-agent",
                Content = longResponse,
                Success = true,
                ExecutionTimeMs = 150
            }
        };

        // Act
        var aggregator = new ResultAggregatorExecutor(
            NullLogger<ResultAggregatorExecutor>.Instance,
            Options.Create(_aggregatorOptions));

        var result = await aggregator.HandleAsync(dispatchResponses, context);

        // Assert: Verify full response is included
        Assert.NotNull(result);
        Assert.Equal(longResponse, result);
    }

    [Fact]
    public async Task Scenario_AgentPriorityOrdering_RespectsPriority()
    {
        // Arrange: Test with custom priority
        var customOptions = new ResultAggregatorOptions
        {
            AgentPriority = ["music-agent", "climate-agent", "light-agent"],
            DefaultSuccessTemplate = "Completed: {0}",
            DefaultFallbackMessage = "Unable to process",
            DefaultFailureMessage = "Failed"
        };

        var context = new RecordingWorkflowContext();

        // Responses added out of priority order
        var dispatchResponses = new List<AgentResponse>
        {
            new AgentResponse
            {
                AgentId = "light-agent",
                Content = "Lights adjusted",
                Success = true,
                ExecutionTimeMs = 100
            },
            new AgentResponse
            {
                AgentId = "music-agent",
                Content = "Music started",
                Success = true,
                ExecutionTimeMs = 150
            },
            new AgentResponse
            {
                AgentId = "climate-agent",
                Content = "Temperature set",
                Success = true,
                ExecutionTimeMs = 200
            }
        };

        // Act
        var aggregator = new ResultAggregatorExecutor(
            NullLogger<ResultAggregatorExecutor>.Instance,
            Options.Create(customOptions));

        var result = await aggregator.HandleAsync(dispatchResponses, context);

        // Assert: Music should come first per priority
        var musicIndex = result.IndexOf("Music started", StringComparison.Ordinal);
        var climateIndex = result.IndexOf("Temperature set", StringComparison.Ordinal);
        var lightIndex = result.IndexOf("Lights adjusted", StringComparison.Ordinal);

        Assert.InRange(musicIndex, 0, climateIndex - 1);
        Assert.InRange(climateIndex, musicIndex + 1, lightIndex - 1);
    }

    [Fact]
    public async Task Scenario_RapidSequentialRequests_MultipleExecutions()
    {
        // Arrange: Simulate rapid sequential requests
        var context = new RecordingWorkflowContext();

        // First request: Lights
        var request1 = new List<AgentResponse>
        {
            new AgentResponse
            {
                AgentId = "light-agent",
                Content = "Turned on kitchen lights",
                Success = true,
                ExecutionTimeMs = 80
            }
        };

        // Second request: Lights + Music
        var request2 = new List<AgentResponse>
        {
            new AgentResponse
            {
                AgentId = "light-agent",
                Content = "Turned on bedroom lights",
                Success = true,
                ExecutionTimeMs = 90
            },
            new AgentResponse
            {
                AgentId = "music-agent",
                Content = "Playing upbeat music",
                Success = true,
                ExecutionTimeMs = 120
            }
        };

        var aggregator = new ResultAggregatorExecutor(
            NullLogger<ResultAggregatorExecutor>.Instance,
            Options.Create(_aggregatorOptions));

        // Act - First aggregation
        var result1 = await aggregator.HandleAsync(request1, context);

        // Act - Second aggregation (independent context)
        var context2 = new RecordingWorkflowContext();
        var result2 = await aggregator.HandleAsync(request2, context2);

        // Assert - Both results independent and correct
        Assert.Contains("kitchen", result1);
        Assert.Contains("bedroom", result2);
        Assert.Contains("upbeat", result2);
    }
}
