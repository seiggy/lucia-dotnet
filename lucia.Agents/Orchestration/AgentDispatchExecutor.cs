using System.Diagnostics;
using lucia.Agents.Orchestration.Models;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucia.Agents.Orchestration;

/// <summary>
/// Executor for dispatching user requests to multiple agents and collecting responses.
/// Implements the MagenticOne pattern for multi-agent workflow coordination.
/// </summary>
public sealed class AgentDispatchExecutor : Executor
{
    private const string ClarificationSystemPrompt =
        """
        You are a friendly home assistant. The system couldn't confidently determine which 
        capability should handle the user's request. Your job is to ask the user a brief, 
        natural clarification question so we can help them.

        Rules:
        - Never mention agent names, internal routing, or system internals.
        - Frame the question in terms of what the user might want to do (lights, music, 
          temperature, fans, timers, etc.).
        - Keep it to 1-2 sentences maximum.
        - Be conversational and helpful, not robotic.
        """;

    private readonly IReadOnlyDictionary<string, IAgentInvoker> _invokers;
    private readonly IChatClient? _chatClient;
    private readonly ILogger<AgentDispatchExecutor> _logger;
    private readonly IOrchestratorObserver? _observer;
    private readonly string _clarificationAgentId;
    private ChatMessage? _userMessage;

    /// <summary>
    /// Creates a new AgentDispatchExecutor instance.
    /// </summary>
    /// <param name="invokers">Dictionary of agent ID to invoker for available agents</param>
    /// <param name="logger">Logger for diagnostic output</param>
    /// <param name="routerOptions">Router options containing clarification agent configuration</param>
    /// <param name="chatClient">Optional chat client for generating natural clarification prompts</param>
    /// <param name="observer">Optional orchestrator observer for trace capture</param>
    public AgentDispatchExecutor(
        IReadOnlyDictionary<string, IAgentInvoker> invokers,
        ILogger<AgentDispatchExecutor> logger,
        IOptions<RouterExecutorOptions> routerOptions,
        IChatClient? chatClient = null,
        IOrchestratorObserver? observer = null)
        : base("AgentDispatch")
    {
        _invokers = invokers ?? throw new ArgumentNullException(nameof(invokers));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _clarificationAgentId = routerOptions?.Value.ClarificationAgentId
            ?? RouterExecutorOptions.DefaultClarificationAgentId;
        _chatClient = chatClient;
        _observer = observer;
    }

    protected override ProtocolBuilder ConfigureProtocol(ProtocolBuilder protocolBuilder)
        => protocolBuilder.ConfigureRoutes(rb => rb.AddHandler<AgentChoiceResult, List<OrchestratorAgentResponse>>(HandleAsync));

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
            await _observer.OnRoutingCompletedAsync(agentChoice, agentChoice.RouterSystemPrompt, cancellationToken).ConfigureAwait(false);
        }

        if (_userMessage is null)
        {
            _logger.LogWarning("User message unavailable when dispatching agent execution.");
            return [CreateFailureResponse(agentChoice.AgentId, "Unable to locate the original user request.")];
        }

        // Short-circuit clarification: use the LLM to craft a natural clarification
        // question instead of dispatching to a non-existent agent
        if (string.Equals(agentChoice.AgentId, _clarificationAgentId, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation(
                "Clarification requested (confidence={Confidence}). Generating natural clarification prompt.",
                agentChoice.Confidence);

            var clarificationText = await GenerateClarificationAsync(
                _userMessage, agentChoice.Reasoning, cancellationToken).ConfigureAwait(false);

            return
            [
                new OrchestratorAgentResponse
                {
                    AgentId = _clarificationAgentId,
                    Content = clarificationText,
                    Success = true,
                    NeedsInput = true,
                    ExecutionTimeMs = 0
                }
            ];
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
    /// Uses the LLM to reword router reasoning into a natural, user-friendly clarification question.
    /// Falls back to a generic prompt if the LLM call fails or no chat client is available.
    /// </summary>
    private async Task<string> GenerateClarificationAsync(
        ChatMessage userMessage, string routerReasoning, CancellationToken cancellationToken)
    {
        if (_chatClient is null)
        {
            _logger.LogDebug("No chat client available for clarification rewriting; using fallback.");
            return "I'm not quite sure what you're asking me to do. Could you give me a bit more detail?";
        }

        var start = Stopwatch.GetTimestamp();
        try
        {
            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, ClarificationSystemPrompt),
                new(ChatRole.User,
                    $"""
                    The user said: "{userMessage.Text}"
                    
                    Internal routing context (do NOT expose this to the user): {routerReasoning}
                    
                    Write a short, friendly clarification question for the user.
                    """)
            };

            var response = await _chatClient.GetResponseAsync(
                messages,
                new ChatOptions { MaxOutputTokens = 128, Temperature = 0.7f },
                cancellationToken).ConfigureAwait(false);

            var text = response.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(text))
            {
                _logger.LogInformation("Generated clarification prompt in {ElapsedMs}ms", 
                    Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                return text;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate clarification prompt via LLM; using fallback.");
        }

        return "I'm not quite sure what you're asking me to do. Could you give me a bit more detail?";
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
