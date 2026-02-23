using lucia.Agents.Orchestration.Models;
using lucia.Agents.Training.Models;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.Orchestration;

/// <summary>
/// Observer that publishes orchestration lifecycle events to the
/// <see cref="LiveActivityChannel"/> for real-time SSE streaming to the dashboard.
/// Emits full granular events for in-process agents and simplified "Processing..."
/// events for remote A2A agents.
/// </summary>
public sealed class LiveActivityObserver : IOrchestratorObserver
{
    private readonly LiveActivityChannel _channel;
    private readonly ILogger<LiveActivityObserver> _logger;

    public LiveActivityObserver(
        LiveActivityChannel channel,
        ILogger<LiveActivityObserver> logger)
    {
        _channel = channel;
        _logger = logger;
    }

    public Task OnRequestStartedAsync(
        string userRequest,
        IReadOnlyList<TracedMessage>? conversationHistory = null,
        CancellationToken cancellationToken = default)
    {
        _channel.Write(new LiveEvent
        {
            Type = LiveEvent.Types.RequestStart,
            AgentName = "orchestrator",
            State = LiveEvent.States.ProcessingPrompt,
            Message = userRequest.Length > 100
                ? string.Concat(userRequest.AsSpan(0, 100), "…")
                : userRequest,
        });

        return Task.CompletedTask;
    }

    public Task OnRoutingCompletedAsync(
        AgentChoiceResult result,
        string? systemPrompt = null,
        CancellationToken cancellationToken = default)
    {
        // Routing decision
        _channel.Write(new LiveEvent
        {
            Type = LiveEvent.Types.Routing,
            AgentName = result.AgentId,
            State = LiveEvent.States.ProcessingPrompt,
            Confidence = result.Confidence,
            Message = result.Reasoning,
        });

        // Emit agentStart for the primary agent
        _channel.Write(new LiveEvent
        {
            Type = LiveEvent.Types.AgentStart,
            AgentName = result.AgentId,
            State = LiveEvent.States.ProcessingPrompt,
        });

        // Emit agentStart for any additional agents (multi-domain)
        if (result.AdditionalAgents is { Count: > 0 })
        {
            foreach (var agentId in result.AdditionalAgents)
            {
                _channel.Write(new LiveEvent
                {
                    Type = LiveEvent.Types.AgentStart,
                    AgentName = agentId,
                    State = LiveEvent.States.ProcessingPrompt,
                });
            }
        }

        return Task.CompletedTask;
    }

    public Task OnAgentExecutionCompletedAsync(
        OrchestratorAgentResponse response,
        CancellationToken cancellationToken = default)
    {
        if (!response.Success)
        {
            _channel.Write(new LiveEvent
            {
                Type = LiveEvent.Types.Error,
                AgentName = response.AgentId,
                State = LiveEvent.States.Error,
                ErrorMessage = response.ErrorMessage,
                DurationMs = response.ExecutionTimeMs,
            });
        }
        else
        {
            _channel.Write(new LiveEvent
            {
                Type = LiveEvent.Types.AgentComplete,
                AgentName = response.AgentId,
                State = LiveEvent.States.GeneratingResponse,
                DurationMs = response.ExecutionTimeMs,
            });
        }

        return Task.CompletedTask;
    }

    public Task OnResponseAggregatedAsync(
        string aggregatedResponse,
        CancellationToken cancellationToken = default)
    {
        _channel.Write(new LiveEvent
        {
            Type = LiveEvent.Types.RequestComplete,
            AgentName = "orchestrator",
            State = LiveEvent.States.Idle,
        });

        return Task.CompletedTask;
    }
}
