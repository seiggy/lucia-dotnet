using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using lucia.Agents.Orchestration.Models;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Reflection;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.Orchestration;

/// <summary>
/// Executor for dispatching user requests to multiple agents and collecting responses.
/// Implements the MagenticOne pattern for multi-agent workflow coordination.
/// </summary>
public class AgentDispatchExecutor : ReflectingExecutor<AgentDispatchExecutor>, IMessageHandler<AgentChoiceResult, List<AgentResponse>>
{
    private readonly Dictionary<string, AgentExecutorWrapper> _wrappers;
    private readonly ILogger<AgentDispatchExecutor> _logger;
    private ChatMessage? _userMessage;

    /// <summary>
    /// Creates a new AgentDispatchExecutor instance
    /// </summary>
    /// <param name="wrappers">Dictionary of agent ID to AgentExecutorWrapper for available agents</param>
    /// <param name="logger">Logger for diagnostic output</param>
    /// <exception cref="ArgumentNullException">Thrown if wrappers or logger is null</exception>
    public AgentDispatchExecutor(Dictionary<string, AgentExecutorWrapper> wrappers, ILogger<AgentDispatchExecutor> logger)
        : base("AgentDispatchExecutor")
    {
        _wrappers = wrappers ?? throw new ArgumentNullException(nameof(wrappers));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Sets the user message for subsequent agent invocations
    /// </summary>
    /// <param name="message">The user's chat message</param>
    public void SetUserMessage(ChatMessage message)
    {
        _userMessage = message;
    }

    /// <summary>
    /// Dispatches the primary agent selection and any additional agents sequentially
    /// </summary>
    /// <param name="agentChoice">The router's choice of agent(s) to invoke</param>
    /// <param name="context">The workflow execution context</param>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    /// <returns>List of AgentResponse objects from all executed agents</returns>
    public ValueTask<List<AgentResponse>> HandleAsync(
        AgentChoiceResult agentChoice,
        IWorkflowContext context,
        CancellationToken cancellationToken)
    {
        return new ValueTask<List<AgentResponse>>(HandleAsyncCore(agentChoice, context, cancellationToken));
    }

    private async Task<List<AgentResponse>> HandleAsyncCore(
        AgentChoiceResult agentChoice,
        IWorkflowContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(agentChoice);
        ArgumentNullException.ThrowIfNull(context);

        _logger.LogInformation("AgentDispatchExecutor: Dispatching to primary agent '{AgentId}' (confidence: {Confidence})",
            agentChoice.AgentId, agentChoice.Confidence);

        var responses = new List<AgentResponse>();

        if (_userMessage == null)
        {
            _logger.LogWarning("AgentDispatchExecutor: User message not set");
            return responses;
        }

        // Execute primary agent
        if (_wrappers.TryGetValue(agentChoice.AgentId, out var primaryWrapper))
        {
            try
            {
                var primaryResponse = await primaryWrapper.HandleAsync(_userMessage, context, cancellationToken)
                    .ConfigureAwait(false);
                responses.Add(primaryResponse);

                _logger.LogInformation("AgentDispatchExecutor: Primary agent '{AgentId}' completed (success: {Success})",
                    agentChoice.AgentId, primaryResponse.Success);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AgentDispatchExecutor: Error executing primary agent '{AgentId}'", agentChoice.AgentId);
                responses.Add(new AgentResponse
                {
                    AgentId = agentChoice.AgentId,
                    Content = $"Error executing agent: {ex.Message}",
                    Success = false,
                    ExecutionTimeMs = 0
                });
            }
        }
        else
        {
            _logger.LogWarning("AgentDispatchExecutor: Primary agent '{AgentId}' not found in wrappers", agentChoice.AgentId);
        }

        // Execute additional agents sequentially if specified
        if (agentChoice.AdditionalAgents != null && agentChoice.AdditionalAgents.Count > 0)
        {
            foreach (var additionalAgentId in agentChoice.AdditionalAgents)
            {
                if (string.IsNullOrWhiteSpace(additionalAgentId))
                {
                    continue;
                }

                if (!_wrappers.TryGetValue(additionalAgentId, out var additionalWrapper))
                {
                    _logger.LogWarning("AgentDispatchExecutor: Additional agent '{AgentId}' not found, skipping",
                        additionalAgentId);
                    continue;
                }

                try
                {
                    _logger.LogInformation("AgentDispatchExecutor: Executing additional agent '{AgentId}'", additionalAgentId);

                    var additionalResponse = await additionalWrapper.HandleAsync(_userMessage, context, cancellationToken)
                        .ConfigureAwait(false);
                    responses.Add(additionalResponse);

                    _logger.LogInformation("AgentDispatchExecutor: Additional agent '{AgentId}' completed (success: {Success})",
                        additionalAgentId, additionalResponse.Success);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "AgentDispatchExecutor: Error executing additional agent '{AgentId}'", additionalAgentId);
                    responses.Add(new AgentResponse
                    {
                        AgentId = additionalAgentId,
                        Content = $"Error executing agent: {ex.Message}",
                        Success = false,
                        ExecutionTimeMs = 0
                    });
                }
            }
        }

        _logger.LogInformation("AgentDispatchExecutor: Completed with {ResponseCount} responses", responses.Count);

        return responses;
    }
}
