using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using lucia.Agents.Orchestration.Models;
using lucia.Agents.Training;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.Orchestration;

/// <summary>
/// Executor for dispatching user requests to multiple agents and collecting responses.
/// Implements the MagenticOne pattern for multi-agent workflow coordination.
/// </summary>
public sealed class AgentDispatchExecutor : Executor
{
    private readonly IReadOnlyDictionary<string, IAgentInvoker> _invokers;
    private readonly ILogger<AgentDispatchExecutor> _logger;
    private readonly IOrchestratorObserver? _observer;
    private ChatMessage? _userMessage;

    /// <summary>
    /// Creates a new AgentDispatchExecutor instance.
    /// </summary>
    /// <param name="invokers">Dictionary of agent ID to invoker for available agents</param>
    /// <param name="logger">Logger for diagnostic output</param>
    /// <param name="observer">Optional orchestrator observer for trace capture</param>
    public AgentDispatchExecutor(
        IReadOnlyDictionary<string, IAgentInvoker> invokers,
        ILogger<AgentDispatchExecutor> logger,
        IOrchestratorObserver? observer = null)
        : base("AgentDispatch")
    {
        _invokers = invokers ?? throw new ArgumentNullException(nameof(invokers));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _observer = observer;
    }

    protected override RouteBuilder ConfigureRoutes(RouteBuilder routeBuilder)
        => routeBuilder.AddHandler<AgentChoiceResult, List<OrchestratorAgentResponse>>(HandleAsync);

    /// <summary>
    /// Sets the user message for subsequent agent invocations
    /// </summary>
    /// <param name="message">The user's chat message</param>
    public void SetUserMessage(ChatMessage message)
    {
        _userMessage = message;
    }

    /// <summary>
    /// Dispatches agents in parallel and collects responses.
    /// </summary>
    public async ValueTask<List<OrchestratorAgentResponse>> HandleAsync(
        AgentChoiceResult agentChoice,
        IWorkflowContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(agentChoice);
        ArgumentNullException.ThrowIfNull(context);

        await context.AddEventAsync(new ExecutorInvokedEvent(this.Id, agentChoice), cancellationToken).ConfigureAwait(false);

        if (_observer is not null)
        {
            await _observer.OnRoutingCompletedAsync(agentChoice, cancellationToken).ConfigureAwait(false);
        }

        if (_userMessage is null)
        {
            _logger.LogWarning("User message unavailable when dispatching agent execution.");
            return [CreateFailureResponse(agentChoice.AgentId, "Unable to locate the original user request.")];
        }

        var executionOrder = BuildExecutionOrder(agentChoice);

        // Dispatch all agents in parallel
        var tasks = new List<Task<OrchestratorAgentResponse>>(executionOrder.Count);
        foreach (var agentId in executionOrder)
        {
            var agentMessage = GetAgentMessage(agentChoice, agentId, _userMessage);
            tasks.Add(InvokeAgentAsync(agentId, agentMessage, context, cancellationToken).AsTask());
        }

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        var responses = new List<OrchestratorAgentResponse>(results);

        // Notify observer for each completed agent
        if (_observer is not null)
        {
            foreach (var response in responses)
            {
                await _observer.OnAgentExecutionCompletedAsync(response, cancellationToken).ConfigureAwait(false);
            }
        }

        if (responses.Count == 0)
        {
            responses.Add(CreateFailureResponse(agentChoice.AgentId, "No agents were dispatched."));
        }

        _logger.LogInformation("AgentDispatchExecutor: Completed {ResponseCount} agents in parallel.", responses.Count);
        return responses;
    }

    /// <summary>
    /// Returns a per-agent tailored message if the router provided agent-specific instructions,
    /// otherwise falls back to the original user message.
    /// </summary>
    private ChatMessage GetAgentMessage(AgentChoiceResult agentChoice, string agentId, ChatMessage fallback)
    {
        if (agentChoice.AgentInstructions is { Count: > 0 })
        {
            var match = agentChoice.AgentInstructions
                .FirstOrDefault(ai => string.Equals(ai.AgentId, agentId, StringComparison.OrdinalIgnoreCase));

            if (match is not null && !string.IsNullOrWhiteSpace(match.Instruction))
            {
                _logger.LogInformation("Dispatching tailored instruction to '{AgentId}': {Instruction}",
                    agentId, match.Instruction);
                return new ChatMessage(ChatRole.User, match.Instruction);
            }
        }

        _logger.LogInformation("No tailored instruction for '{AgentId}', using full user message.", agentId);
        return fallback;
    }

    private async ValueTask<OrchestratorAgentResponse> InvokeAgentAsync(
        string agentId,
        ChatMessage userMessage,
        IWorkflowContext context,
        CancellationToken cancellationToken)
    {
        if (!_invokers.TryGetValue(agentId, out var invoker))
        {
            _logger.LogWarning("No invoker registered for agent {AgentId}.", agentId);
            return CreateFailureResponse(agentId, $"Agent '{agentId}' is not available.");
        }

        try
        {
            return await invoker.InvokeAsync(userMessage, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Agent '{AgentId}' execution failed.", agentId);
            return CreateFailureResponse(agentId, ex.Message);
        }
    }

    private static OrchestratorAgentResponse CreateFailureResponse(string agentId, string error)
        => new()
        {
            AgentId = agentId,
            Content = string.Empty,
            Success = false,
            ErrorMessage = error,
            ExecutionTimeMs = 0
        };

    private static IReadOnlyList<string> BuildExecutionOrder(AgentChoiceResult choice)
    {
        var ordered = new List<string>();
        if (!string.IsNullOrWhiteSpace(choice.AgentId))
        {
            ordered.Add(choice.AgentId);
        }

        if (choice.AdditionalAgents is { Count: > 0 })
        {
            foreach (var agentId in choice.AdditionalAgents)
            {
                if (!string.IsNullOrWhiteSpace(agentId) && !ordered.Contains(agentId, StringComparer.OrdinalIgnoreCase))
                {
                    ordered.Add(agentId);
                }
            }
        }

        return ordered;
    }
}
