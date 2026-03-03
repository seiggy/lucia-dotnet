using System.Diagnostics;
using A2A;
using lucia.Agents.Abstractions;
using lucia.Agents.Orchestration.Models;
using lucia.Agents.Registry;
using lucia.Agents.Training.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucia.Agents.Orchestration;

/// <summary>
/// Main orchestrator for the Lucia multi-agent system using MagenticOne pattern.
/// Coordinates session management, workflow execution, and task lifecycle.
/// Delegates session/task persistence to <see cref="SessionManager"/> and
/// workflow building/execution to <see cref="WorkflowFactory"/>.
/// </summary>
public class LuciaEngine
{
    private readonly IAgentRegistry _agentRegistry;
    private readonly SessionManager _sessionManager;
    private readonly WorkflowFactory _workflowFactory;
    private readonly IOptions<ResultAggregatorOptions> _aggregatorOptions;
    private readonly ILogger<LuciaEngine> _logger;
    private readonly IOrchestratorObserver? _observer;
    private static readonly ActivitySource ActivitySource = new("Lucia.Agents.Orchestration.LuciaEngine", "1.0.0");

    public LuciaEngine(
        IAgentRegistry agentRegistry,
        SessionManager sessionManager,
        WorkflowFactory workflowFactory,
        IOptions<ResultAggregatorOptions> aggregatorOptions,
        ILogger<LuciaEngine> logger,
        IOrchestratorObserver? observer = null)
    {
        _agentRegistry = agentRegistry ?? throw new ArgumentNullException(nameof(agentRegistry));
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _workflowFactory = workflowFactory ?? throw new ArgumentNullException(nameof(workflowFactory));
        _aggregatorOptions = aggregatorOptions ?? throw new ArgumentNullException(nameof(aggregatorOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _observer = observer;
    }

    /// <summary>
    /// Process a user request through the MagenticOne multi-agent system
    /// </summary>
    /// <param name="userRequest">The user's request message</param>
    /// <param name="taskId">Optional task ID for conversation continuity</param>
    /// <param name="sessionId">Optional session ID for grouping related tasks</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The orchestrated response from the agent system</returns>
    public async Task<OrchestratorResult> ProcessRequestAsync(
        string userRequest,
        string? taskId = null,
        string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        var activity = ActivitySource.StartActivity();
        activity?.AddBaggage(nameof(userRequest), userRequest);
        _logger.LogInformation("Processing user request: {Request} (TaskId: {TaskId}, SessionId: {SessionId})",
            userRequest, taskId ?? "new", sessionId ?? "new");

        string? requestId = null;
        try
        {
            if (string.IsNullOrWhiteSpace(userRequest))
            {
                throw new ArgumentException("User request cannot be empty.", nameof(userRequest));
            }

            // 1. Session & task lifecycle — load in parallel since they're independent
            activity?.AddEvent(new ActivityEvent("SessionRehydrateStart"));
            var sessionTask = _sessionManager.LoadSessionAsync(sessionId, cancellationToken);
            var agentTaskTask = _sessionManager.LoadOrCreateTaskAsync(taskId, sessionId, cancellationToken);
            await Task.WhenAll(sessionTask, agentTaskTask).ConfigureAwait(false);

            var sessionData = sessionTask.Result;
            var agentTask = agentTaskTask.Result;
            activity?.AddEvent(new ActivityEvent("SessionRehydrateEnd"));
            
            // Add user message to task history
            var userMessage = new AgentMessage
            {
                Role = MessageRole.User,
                MessageId = Guid.NewGuid().ToString("N"),
                TaskId = agentTask.Id,
                ContextId = agentTask.ContextId,
                Parts = new List<Part> { new TextPart { Text = userRequest } }
            };
            activity?.SetTag("agent.MessageId", userMessage.MessageId);
            activity?.SetTag("agent.TaskId", userMessage.TaskId);
            activity?.SetTag("agent.SessionId", sessionData?.SessionId);
            activity?.SetTag("agent.ContextId", agentTask.ContextId);
            
            agentTask.History ??= new List<AgentMessage>();
            agentTask.History.Add(userMessage);

            // 2. Agent resolution & task status update in parallel — they're independent
            activity?.AddEvent(new ActivityEvent("RetrieveAgentsStart"));
            var updateWorkingTask = _sessionManager.UpdateTaskStatusAsync(
                agentTask.Id, TaskState.Working, message: userMessage, final: false, cancellationToken);
            var agentCardsTask = _agentRegistry
                .GetAllAgentsAsync(cancellationToken);

            await Task.WhenAll(updateWorkingTask, agentCardsTask).ConfigureAwait(false);
            activity?.AddEvent(new ActivityEvent("RetrieveAgentsCompleted"));

            var availableAgentCards = agentCardsTask.Result;

            if (availableAgentCards.Count == 0)
            {
                _logger.LogWarning("No agents available to process request");
                var noAgentsMessage = "I don't have any specialized agents available right now. Please try again later.";
                await FailTaskAsync(agentTask, noAgentsMessage, cancellationToken).ConfigureAwait(false);
                activity?.SetStatus(ActivityStatusCode.Error);
                return new OrchestratorResult { Text = noAgentsMessage };
            }

            var resolvedAgents = await _workflowFactory.ResolveAgentsAsync(cancellationToken)
                .ConfigureAwait(false);
            var invokers = _workflowFactory.CreateAgentInvokers(availableAgentCards, resolvedAgents);

            if (invokers.Count == 0)
            {
                _logger.LogWarning("Unable to build any agent invokers. Falling back to aggregator message.");
                var fallbackMessage = _aggregatorOptions.Value.DefaultFallbackMessage;
                await FailTaskAsync(agentTask, fallbackMessage, cancellationToken).ConfigureAwait(false);
                activity?.SetStatus(ActivityStatusCode.Error);
                return new OrchestratorResult { Text = fallbackMessage };
            }

            var historyAwareRequest = SessionManager.BuildHistoryAwareRequest(sessionData, userRequest);

            // Initialize trace and capture request ID for correlation
            if (_observer is not null)
            {
                var historyMessages = sessionData?.History
                    .Select(t => new TracedMessage
                    {
                        Role = t.Role,
                        Content = t.Content,
                        Timestamp = t.Timestamp
                    })
                    .ToList();

                requestId = await _observer.OnRequestStartedAsync(userRequest, historyMessages, cancellationToken).ConfigureAwait(false);
            }

            var workflowResult = await _workflowFactory.BuildAndExecuteAsync(
                invokers, historyAwareRequest, requestId, cancellationToken).ConfigureAwait(false);

            // 3. Post-processing
            var finalText = workflowResult?.Text ?? _aggregatorOptions.Value.DefaultFallbackMessage;
            var needsInput = workflowResult?.NeedsInput ?? false;

            if (needsInput)
            {
                _logger.LogInformation("Conversation requires user clarification for task {TaskId}", agentTask.Id);
            }

            if (_observer is not null && requestId is not null)
            {
                await _observer.OnResponseAggregatedAsync(requestId, finalText, cancellationToken).ConfigureAwait(false);
            }

            // Add assistant response to task history and update status
            var assistantMessage = new AgentMessage
            {
                Role = MessageRole.Agent,
                MessageId = Guid.NewGuid().ToString("N"),
                TaskId = agentTask.Id,
                ContextId = agentTask.ContextId,
                Parts = new List<Part> { new TextPart { Text = finalText } }
            };

            agentTask.History.Add(assistantMessage);

            var finalState = needsInput ? TaskState.InputRequired : TaskState.Completed;

            // Persist task status and session in parallel — response is already computed
            await Task.WhenAll(
                _sessionManager.UpdateTaskStatusAsync(
                    agentTask.Id, finalState, message: assistantMessage, final: !needsInput, CancellationToken.None),
                _sessionManager.SaveSessionAsync(
                    sessionId, sessionData, userRequest, finalText, CancellationToken.None)
            ).ConfigureAwait(false);

            _logger.LogInformation("Task {TaskId} ended with state {TaskState} and {HistoryCount} history items",
                agentTask.Id, finalState, agentTask.History.Count);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return new OrchestratorResult { Text = finalText, NeedsInput = needsInput };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing user request: {Request}", userRequest);
            activity?.SetStatus(ActivityStatusCode.Error);
            var errorResponse = "I encountered an error while processing your request. Please try again.";

            // Persist the trace even on failure so errors are visible in the dashboard
            if (_observer is not null && requestId is not null)
            {
                try
                {
                    await _observer.OnResponseAggregatedAsync(requestId, errorResponse, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception traceEx)
                {
                    _logger.LogError(traceEx, "Failed to persist error trace");
                }
            }

            // Try to update task status to failed
            try
            {
                if (taskId is not null || sessionId is not null)
                {
                    var failedTaskId = taskId ?? Guid.NewGuid().ToString();
                    var failedContextId = sessionId ?? Guid.NewGuid().ToString();

                    var errorMessage = new AgentMessage
                    {
                        Role = MessageRole.Agent,
                        MessageId = Guid.NewGuid().ToString("N"),
                        TaskId = failedTaskId,
                        ContextId = failedContextId,
                        Parts = new List<Part> { new TextPart { Text = errorResponse } }
                    };

                    await _sessionManager.UpdateTaskStatusAsync(
                        failedTaskId, TaskState.Failed, errorMessage, true, CancellationToken.None).ConfigureAwait(false);

                    _logger.LogInformation("Task {TaskId} marked as failed", failedTaskId);
                }
            }
            catch (Exception taskEx)
            {
                _logger.LogError(taskEx, "Failed to update task status to Failed");
            }

            return new OrchestratorResult { Text = errorResponse };
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

    private async Task FailTaskAsync(AgentTask agentTask, string message, CancellationToken cancellationToken)
    {
        var errorMessage = new AgentMessage
        {
            Role = MessageRole.Agent,
            MessageId = Guid.NewGuid().ToString("N"),
            TaskId = agentTask.Id,
            ContextId = agentTask.ContextId,
            Parts = new List<Part> { new TextPart { Text = message } }
        };

        await _sessionManager.UpdateTaskStatusAsync(
            agentTask.Id, TaskState.Failed, errorMessage, true, cancellationToken).ConfigureAwait(false);
    }
}
