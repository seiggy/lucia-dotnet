using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using A2A;
using lucia.Agents.Orchestration.Models;
using lucia.Agents.Registry;
using lucia.Agents.Services;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Reflection;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucia.Agents.Orchestration;

/// <summary>
/// Main orchestrator for the Lucia multi-agent system using MagenticOne pattern
/// </summary>
public class LuciaOrchestrator
{
    private readonly IChatClient _chatClient;
    private readonly IAgentRegistry _agentRegistry;
    private readonly IServiceProvider _serviceProvider;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<LuciaOrchestrator> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IOptions<RouterExecutorOptions> _routerOptions;
    private readonly IOptions<AgentExecutorWrapperOptions> _wrapperOptions;
    private readonly IOptions<ResultAggregatorOptions> _aggregatorOptions;
    private readonly TimeProvider _timeProvider;
    private readonly ITaskManager _taskManager;
    private readonly IOrchestratorObserver? _observer;
    private readonly IAgentProvider? _agentProvider;
    private readonly IPromptCacheService? _promptCache;
    private readonly ISessionCacheService? _sessionCache;
    private readonly SessionCacheOptions _sessionCacheOptions;

    public LuciaOrchestrator(
        [FromKeyedServices(OrchestratorServiceKeys.RouterModel)] IChatClient chatClient,
        IAgentRegistry agentRegistry,
        IServiceProvider serviceProvider,
        IHttpClientFactory httpClientFactory,
        ILogger<LuciaOrchestrator> logger,
        ILoggerFactory loggerFactory,
        IOptions<RouterExecutorOptions> routerOptions,
        IOptions<AgentExecutorWrapperOptions> wrapperOptions,
        IOptions<ResultAggregatorOptions> aggregatorOptions,
        IOptions<SessionCacheOptions> sessionCacheOptions,
        TimeProvider timeProvider,
        ITaskManager taskManager,
        IOrchestratorObserver? observer = null,
        IAgentProvider? agentProvider = null,
        IPromptCacheService? promptCache = null,
        ISessionCacheService? sessionCache = null)
    {
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        _agentRegistry = agentRegistry;
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _routerOptions = routerOptions ?? throw new ArgumentNullException(nameof(routerOptions));
        _wrapperOptions = wrapperOptions ?? throw new ArgumentNullException(nameof(wrapperOptions));
        _aggregatorOptions = aggregatorOptions ?? throw new ArgumentNullException(nameof(aggregatorOptions));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _taskManager = taskManager ?? throw new ArgumentNullException(nameof(taskManager));
        _observer = observer;
        _agentProvider = agentProvider;
        _promptCache = promptCache;
        _sessionCache = sessionCache;
        _sessionCacheOptions = sessionCacheOptions?.Value ?? new SessionCacheOptions();

        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// Process a user request through the MagenticOne multi-agent system
    /// </summary>
    /// <param name="userRequest">The user's request message</param>
    /// <param name="taskId">Optional task ID for conversation continuity</param>
    /// <param name="sessionId">Optional session ID for grouping related tasks</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The orchestrated response from the agent system</returns>
    public async Task<string> ProcessRequestAsync(
        string userRequest, 
        string? taskId = null,
        string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Processing user request: {Request} (TaskId: {TaskId}, SessionId: {SessionId})", 
            userRequest, taskId ?? "new", sessionId ?? "new");

        try
        {
            if (string.IsNullOrWhiteSpace(userRequest))
            {
                throw new ArgumentException("User request cannot be empty.", nameof(userRequest));
            }

            // Check prompt cache for an exact or semantic match on routing decision
            // (cache hit skips only the router LLM call; agents still execute tools fresh)

            // Load existing session from Redis for multi-turn conversation support
            SessionData? sessionData = null;
            if (_sessionCache is not null && !string.IsNullOrWhiteSpace(sessionId))
            {
                sessionData = await _sessionCache.GetSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
                if (sessionData is not null)
                {
                    _logger.LogInformation(
                        "Loaded session {SessionId} with {TurnCount} prior turns",
                        sessionId, sessionData.History.Count);
                }
                else
                {
                    _logger.LogDebug("No existing session found for {SessionId}, starting new conversation", sessionId);
                }
            }

            // Load or create AgentTask for durable persistence (T038 - US4)
            AgentTask agentTask;
            if (!string.IsNullOrWhiteSpace(taskId))
            {
                var taskQueryParams = new TaskQueryParams { Id = taskId };
                var existingTask = await _taskManager.GetTaskAsync(taskQueryParams, cancellationToken).ConfigureAwait(false);
                
                if (existingTask != null)
                {
                    _logger.LogInformation("Loaded existing task {TaskId} with {HistoryCount} history items", 
                        existingTask.Id, existingTask.History?.Count ?? 0);
                    agentTask = existingTask;
                }
                else
                {
                    _logger.LogWarning("Task {TaskId} not found, creating new task", taskId);
                    agentTask = await _taskManager.CreateTaskAsync(sessionId, taskId, cancellationToken).ConfigureAwait(false);
                }
            }
            else
            {
                // Create new task
                agentTask = await _taskManager.CreateTaskAsync(sessionId, taskId: null, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Created new task {TaskId} with context {ContextId}", 
                    agentTask.Id, agentTask.ContextId);
            }

            // Add user message to task history
            var userMessage = new AgentMessage
            {
                Role = MessageRole.User,
                MessageId = Guid.NewGuid().ToString("N"),
                TaskId = agentTask.Id,
                ContextId = agentTask.ContextId,
                Parts = new List<Part> { new TextPart { Text = userRequest } }
            };
            
            agentTask.History ??= new List<AgentMessage>();
            agentTask.History.Add(userMessage);

            // Update task status to working
            await _taskManager.UpdateStatusAsync(
                agentTask.Id, 
                TaskState.Working, 
                message: null, 
                final: false, 
                cancellationToken).ConfigureAwait(false);

            var availableAgentCards = await _agentRegistry
                .GetAllAgentsAsync(cancellationToken)
                .ConfigureAwait(false);

            if (availableAgentCards.Count == 0)
            {
                _logger.LogWarning("No agents available to process request");
                var noAgentsMessage = "I don't have any specialized agents available right now. Please try again later.";

                // Update task status to failed
                var errorMessage = new AgentMessage
                {
                    Role = MessageRole.Agent,
                    MessageId = Guid.NewGuid().ToString("N"),
                    TaskId = agentTask.Id,
                    ContextId = agentTask.ContextId,
                    Parts = new List<Part> { new TextPart { Text = noAgentsMessage } }
                };
                
                await _taskManager.UpdateStatusAsync(
                    agentTask.Id,
                    TaskState.Failed,
                    message: errorMessage,
                    final: true,
                    cancellationToken).ConfigureAwait(false);
                
                return noAgentsMessage;
            }

            IReadOnlyList<AIAgent> resolvedAgents;
            if (_agentProvider is not null)
            {
                resolvedAgents = await _agentProvider.GetAgentsAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var aiAgents = await _agentRegistry
                    .GetAllAgentsAsync(cancellationToken)
                    .ConfigureAwait(false);

                var resolved = new List<AIAgent>();

                // Resolve in-process agents from ILuciaAgent DI registrations
                var luciaAgents = _serviceProvider.GetServices<Abstractions.ILuciaAgent>();
                foreach (var luciaAgent in luciaAgents)
                {
                    try
                    {
                        var aiAgent = luciaAgent.GetAIAgent();
                        if (aiAgent is not null)
                        {
                            resolved.Add(aiAgent);
                            _logger.LogDebug("Resolved local AIAgent: {AgentName}", aiAgent.Name ?? aiAgent.Id);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to resolve AIAgent from ILuciaAgent {Type}", luciaAgent.GetType().Name);
                    }
                }

                // Also resolve remote agents with absolute URIs
                foreach (var card in aiAgents)
                {
                    if (Uri.TryCreate(card.Url, UriKind.Absolute, out _))
                    {
                        try
                        {
                            resolved.Add(card.AsAIAgent());
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to resolve AIAgent from card {AgentName} ({Url})", card.Name, card.Url);
                        }
                    }
                }

                resolvedAgents = resolved.AsReadOnly();
            }

            var wrappers = CreateWrappers(availableAgentCards, resolvedAgents);
            if (wrappers.Count == 0)
            {
                _logger.LogWarning("Unable to build any agent executor wrappers. Falling back to aggregator message.");
                var fallbackMessage = _aggregatorOptions.Value.DefaultFallbackMessage;
                
                // Update task status to failed
                var errorMessage = new AgentMessage
                {
                    Role = MessageRole.Agent,
                    MessageId = Guid.NewGuid().ToString("N"),
                    TaskId = agentTask.Id,
                    ContextId = agentTask.ContextId,
                    Parts = new List<Part> { new TextPart { Text = fallbackMessage } }
                };
                
                await _taskManager.UpdateStatusAsync(
                    agentTask.Id,
                    TaskState.Failed,
                    message: errorMessage,
                    final: true,
                    cancellationToken).ConfigureAwait(false);
                
                return fallbackMessage;
            }

            var routerLogger = _loggerFactory.CreateLogger<RouterExecutor>();
            var dispatchLogger = _loggerFactory.CreateLogger<AgentDispatchExecutor>();
            var aggregatorLogger = _loggerFactory.CreateLogger<ResultAggregatorExecutor>();
            var router = new RouterExecutor(_chatClient, _agentRegistry, routerLogger, _routerOptions, _promptCache);
            var dispatch = new AgentDispatchExecutor(wrappers, dispatchLogger, _observer);
            var aggregator = new ResultAggregatorExecutor(aggregatorLogger, _aggregatorOptions);

            // Build conversation history messages for the router to have multi-turn context
            var historyAwareRequest = BuildHistoryAwareRequest(sessionData, userRequest);
            var chatMessage = new ChatMessage(ChatRole.User, historyAwareRequest);
            dispatch.SetUserMessage(chatMessage);

            // Initialize trace in the PARENT async context so AsyncLocal state
            // flows correctly into the workflow child contexts.
            if (_observer is not null)
            {
                await _observer.OnRequestStartedAsync(userRequest, cancellationToken).ConfigureAwait(false);
            }

            var builder = new WorkflowBuilder(router)
                .WithName("LuciaOrchestratorWorkflow")
                .WithDescription("Routes Lucia user requests to specialized agents and aggregates responses.")
                .AddEdge(router, dispatch)
                .AddEdge(dispatch, aggregator)
                .WithOutputFrom(aggregator);

            var workflow = builder.Build();

            var result = await ExecuteWorkflowAsync(workflow, chatMessage, cancellationToken).ConfigureAwait(false);

            var finalResult = result ?? _aggregatorOptions.Value.DefaultFallbackMessage;

            if (_observer is not null)
            {
                await _observer.OnResponseAggregatedAsync(finalResult, cancellationToken).ConfigureAwait(false);
            }

            // Add assistant response to task history and update status to completed (T038 - US4)
            var assistantMessage = new AgentMessage
            {
                Role = MessageRole.Agent,
                MessageId = Guid.NewGuid().ToString("N"),
                TaskId = agentTask.Id,
                ContextId = agentTask.ContextId,
                Parts = new List<Part> { new TextPart { Text = finalResult } }
            };

            agentTask.History.Add(assistantMessage);

            await _taskManager.UpdateStatusAsync(
                agentTask.Id,
                TaskState.Completed,
                message: assistantMessage,
                final: true,
                cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Task {TaskId} completed successfully with {HistoryCount} history items",
                agentTask.Id, agentTask.History.Count);

            // Persist session to Redis for multi-turn conversation continuity
            if (_sessionCache is not null && !string.IsNullOrWhiteSpace(sessionId))
            {
                sessionData ??= new SessionData { SessionId = sessionId };
                sessionData.History.Add(new SessionTurn { Role = "user", Content = userRequest });
                sessionData.History.Add(new SessionTurn { Role = "assistant", Content = finalResult });
                await _sessionCache.SaveSessionAsync(sessionData, cancellationToken).ConfigureAwait(false);
            }

            // Routing decisions are cached inside RouterExecutor (not final responses)
            // so agents always execute tools fresh on subsequent identical prompts.

            return finalResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing user request: {Request}", userRequest);
            
            // Try to update task status to failed
            try
            {
                // If agentTask was initialized, update its status
                if (taskId != null || sessionId != null)
                {
                    // Try to determine the taskId from the parameters or create emergency task
                    var failedTaskId = taskId ?? Guid.NewGuid().ToString();
                    var failedContextId = sessionId ?? Guid.NewGuid().ToString();
                    
                    var errorMessage = new AgentMessage
                    {
                        Role = MessageRole.Agent,
                        MessageId = Guid.NewGuid().ToString("N"),
                        TaskId = failedTaskId,
                        ContextId = failedContextId,
                        Parts = new List<Part> { new TextPart { Text = "I encountered an error while processing your request. Please try again." } }
                    };
                    
                    await _taskManager.UpdateStatusAsync(
                        failedTaskId,
                        TaskState.Failed,
                        message: errorMessage,
                        final: true,
                        cancellationToken).ConfigureAwait(false);
                    
                    _logger.LogInformation("Task {TaskId} marked as failed", failedTaskId);
                }
            }
            catch (Exception taskEx)
            {
                _logger.LogError(taskEx, "Failed to update task status to Failed");
            }

            return "I encountered an error while processing your request. Please try again.";
        }
    }

    /// <summary>
    /// Get the current status of the orchestrator
    /// </summary>
    public async Task<OrchestratorStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var agents = await _agentRegistry
            .GetAllAgentsAsync(cancellationToken)
            .ConfigureAwait(false);

        return new OrchestratorStatus
        {
            IsReady = agents.Count > 0,
            AvailableAgentCount = agents.Count,
            AvailableAgents = agents
        };
    }

    /// <summary>
    /// Clear the chat history
    /// </summary>
    public void ClearHistory()
    {
        _logger.LogInformation("Chat history cleared");
        throw new NotImplementedException("Chat history management not implemented yet");
    }

    private Dictionary<string, AgentExecutorWrapper> CreateWrappers(
        IReadOnlyCollection<AgentCard> agentCards,
        IReadOnlyList<AIAgent> aiAgents)
    {
        var wrappers = new Dictionary<string, AgentExecutorWrapper>(StringComparer.OrdinalIgnoreCase);
        var wrapperLogger = _loggerFactory.CreateLogger<AgentExecutorWrapper>();

        var agentsByKey = aiAgents
            .Select(agent => (Key: NormalizeAgentKey(agent), Agent: agent))
            .Where(tuple => tuple.Key is not null)
            .GroupBy(tuple => tuple.Key!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Agent, StringComparer.OrdinalIgnoreCase);

        var cardsByKey = agentCards
            .Where(card => !string.IsNullOrWhiteSpace(card.Name))
            .GroupBy(card => card.Name!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var allKeys = new HashSet<string>(agentsByKey.Keys, StringComparer.OrdinalIgnoreCase);
        allKeys.UnionWith(cardsByKey.Keys);

        foreach (var key in allKeys)
        {
            agentsByKey.TryGetValue(key, out var agent);
            cardsByKey.TryGetValue(key, out var card);

            var wrapper = new AgentExecutorWrapper(
                key,
                _serviceProvider,
                wrapperLogger,
                _wrapperOptions,
                agent,
                card,
                card is not null ? _taskManager : null,
                _timeProvider);

            wrappers[key] = wrapper;
        }

        return wrappers;
    }

    private static string? NormalizeAgentKey(AIAgent agent)
        => agent.Name ?? agent.Id;

    private async Task<string?> ExecuteWorkflowAsync(Workflow workflow, ChatMessage input, CancellationToken cancellationToken)
    {
        var startTimestamp = _timeProvider.GetTimestamp();
        using var activity = OrchestrationTelemetry.Source.StartActivity("LuciaOrchestrator.ExecuteWorkflow", ActivityKind.Internal);
        activity?.SetTag("workflow.name", workflow.Name ?? "LuciaOrchestratorWorkflow");
        activity?.SetTag("workflow.start.executor", workflow.StartExecutorId);

        try
        {
            await using var run = await InProcessExecution.RunAsync(workflow, input, cancellationToken: cancellationToken).ConfigureAwait(false);

            string? result = null;
            List<string>? errors = null;

            foreach (var evt in run.OutgoingEvents.OfType<WorkflowOutputEvent>())
            {
                if (evt.Data is string text)
                {
                    result = text;
                }
            }

            foreach (var evt in run.OutgoingEvents.OfType<WorkflowErrorEvent>())
            {
                var exception = evt.Data as Exception;
                var message = exception?.Message ?? evt.Data?.ToString() ?? "Workflow execution reported an unknown error.";

                errors ??= new List<string>();
                errors.Add(message);

                if (exception is not null)
                {
                    _logger.LogError(exception, "Workflow execution emitted error event: {Message}", message);
                }
                else
                {
                    _logger.LogError("Workflow execution emitted error event: {Message}", message);
                }
            }

            if (errors is not null)
            {
                var errorSummary = string.Join("; ", errors);
                activity?.SetTag(OrchestrationTelemetry.Tags.Success, false);
                activity?.SetTag(OrchestrationTelemetry.Tags.ErrorMessage, errorSummary);

                return result ?? errorSummary;
            }

            activity?.SetTag(OrchestrationTelemetry.Tags.Success, true);

            if (result is not null)
            {
                activity?.SetTag("workflow.output.length", result.Length);
            }

            return result;
        }
        catch (Exception ex)
        {
            activity?.SetTag(OrchestrationTelemetry.Tags.Success, false);
            activity?.SetTag(OrchestrationTelemetry.Tags.ErrorMessage, ex.Message);
            _logger.LogError(ex, "Workflow execution failed to complete.");
            throw;
        }
        finally
        {
            var elapsed = _timeProvider.GetElapsedTime(startTimestamp);
            activity?.SetTag(OrchestrationTelemetry.Tags.ExecutionTime, elapsed.TotalMilliseconds);
        }
    }

    /// <summary>
    /// Prepends conversation history to the user request so the router and agents
    /// have multi-turn context. Returns the original request if no history exists.
    /// </summary>
    private static string BuildHistoryAwareRequest(SessionData? sessionData, string currentRequest)
    {
        if (sessionData is null || sessionData.History.Count == 0)
        {
            return currentRequest;
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[Conversation History]");
        foreach (var turn in sessionData.History)
        {
            sb.Append(turn.Role == "user" ? "User: " : "Assistant: ");
            sb.AppendLine(turn.Content);
        }

        sb.AppendLine();
        sb.AppendLine("[Current Request]");
        sb.Append(currentRequest);

        return sb.ToString();
    }
}
