using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using A2A;
using lucia.Agents.Orchestration.Models;
using lucia.Agents.Registry;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Reflection;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucia.Agents.Orchestration;

/// <summary>
/// Main orchestrator for the Lucia multi-agent system using MagenticOne pattern
/// </summary>
public class LuciaOrchestrator
{
    private readonly IChatClient _chatClient;
    private readonly AgentRegistry _agentRegistry;
    private readonly AgentCatalog _agentCatalog;
    private readonly IServiceProvider _serviceProvider;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<LuciaOrchestrator> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IOptions<RouterExecutorOptions> _routerOptions;
    private readonly IOptions<AgentExecutorWrapperOptions> _wrapperOptions;
    private readonly IOptions<ResultAggregatorOptions> _aggregatorOptions;
    private readonly TimeProvider _timeProvider;
    private readonly ITaskManager _taskManager;

    public LuciaOrchestrator(
        IChatClient chatClient,
        AgentRegistry agentRegistry,
        AgentCatalog agentCatalog,
        IServiceProvider serviceProvider,
        IHttpClientFactory httpClientFactory,
        ILogger<LuciaOrchestrator> logger,
        ILoggerFactory loggerFactory,
        IOptions<RouterExecutorOptions> routerOptions,
        IOptions<AgentExecutorWrapperOptions> wrapperOptions,
        IOptions<ResultAggregatorOptions> aggregatorOptions,
        TimeProvider timeProvider,
        ITaskManager taskManager)
    {
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        _agentRegistry = agentRegistry;
        _agentCatalog = agentCatalog ?? throw new ArgumentNullException(nameof(agentCatalog));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _routerOptions = routerOptions ?? throw new ArgumentNullException(nameof(routerOptions));
        _wrapperOptions = wrapperOptions ?? throw new ArgumentNullException(nameof(wrapperOptions));
        _aggregatorOptions = aggregatorOptions ?? throw new ArgumentNullException(nameof(aggregatorOptions));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _taskManager = taskManager ?? throw new ArgumentNullException(nameof(taskManager));

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
                .GetAgentsAsync(cancellationToken)
                .ToListAsync(cancellationToken)
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

            var aiAgents = await _agentCatalog
                .GetAgentsAsync(cancellationToken)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var wrappers = CreateWrappers(availableAgentCards, aiAgents);
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
            var router = new RouterExecutor(_chatClient, _agentRegistry, routerLogger, _routerOptions);
            var dispatch = new AgentDispatchExecutor(wrappers, dispatchLogger);
            var aggregator = new ResultAggregatorExecutor(aggregatorLogger, _aggregatorOptions);

            var chatMessage = new ChatMessage(ChatRole.User, userRequest);
            dispatch.SetUserMessage(chatMessage);

            var builder = new WorkflowBuilder(router)
                .WithName("LuciaOrchestratorWorkflow")
                .WithDescription("Routes Lucia user requests to specialized agents and aggregates responses.")
                .AddEdge(router, dispatch)
                .AddEdge(dispatch, aggregator)
                .WithOutputFrom(aggregator);

            var workflow = builder.Build();

            var result = await ExecuteWorkflowAsync(workflow, chatMessage, cancellationToken).ConfigureAwait(false);

            var finalResult = result ?? _aggregatorOptions.Value.DefaultFallbackMessage;

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
            .GetAgentsAsync(cancellationToken)
            .ToListAsync(cancellationToken)
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
        IReadOnlyList<AgentCard> agentCards,
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

    private sealed class AgentDispatchExecutor : ReflectingExecutor<AgentDispatchExecutor>, IMessageHandler<AgentChoiceResult, List<AgentResponse>>
    {
        public const string ExecutorId = "AgentDispatch";

        private readonly IReadOnlyDictionary<string, AgentExecutorWrapper> _wrappers;
        private readonly ILogger<AgentDispatchExecutor> _logger;
        private ChatMessage? _userMessage;

        public AgentDispatchExecutor(
            IReadOnlyDictionary<string, AgentExecutorWrapper> wrappers,
            ILogger<AgentDispatchExecutor> logger)
            : base(ExecutorId)
        {
            _wrappers = wrappers;
            _logger = logger;
        }

        public void SetUserMessage(ChatMessage message)
        {
            _userMessage = message;
        }

        public async ValueTask<List<AgentResponse>> HandleAsync(AgentChoiceResult message, IWorkflowContext context, CancellationToken cancellationToken)
        {
            await context.AddEventAsync(new ExecutorInvokedEvent(this.Id, message), cancellationToken).ConfigureAwait(false);

            if (_userMessage is null)
            {
                _logger.LogWarning("User message unavailable when dispatching agent execution.");
                return new List<AgentResponse> { CreateFailureResponse(message.AgentId, "Unable to locate the original user request.") };
            }

            var executionOrder = BuildExecutionOrder(message);
            var responses = new List<AgentResponse>(executionOrder.Count);

            foreach (var agentId in executionOrder)
            {
                var response = await InvokeAgentAsync(agentId, _userMessage, context, cancellationToken).ConfigureAwait(false);
                responses.Add(response);
            }

            if (responses.Count == 0)
            {
                responses.Add(CreateFailureResponse(message.AgentId, "No agents were dispatched."));
            }

            return responses;
        }

        public ValueTask<List<AgentResponse>> HandleAsync(AgentChoiceResult message, IWorkflowContext context)
            => HandleAsync(message, context, CancellationToken.None);

        private async ValueTask<AgentResponse> InvokeAgentAsync(string agentId, ChatMessage userMessage, IWorkflowContext context, CancellationToken cancellationToken)
        {
            if (!_wrappers.TryGetValue(agentId, out var wrapper))
            {
                _logger.LogWarning("No wrapper registered for agent {AgentId}.", agentId);
                return CreateFailureResponse(agentId, $"Agent '{agentId}' is not available.");
            }

            return await wrapper.HandleAsync(userMessage, context, cancellationToken).ConfigureAwait(false);
        }

        private static AgentResponse CreateFailureResponse(string agentId, string error)
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
}
