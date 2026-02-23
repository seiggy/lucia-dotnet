using A2A;
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
        _logger.LogInformation("Processing user request: {Request} (TaskId: {TaskId}, SessionId: {SessionId})",
            userRequest, taskId ?? "new", sessionId ?? "new");

        try
        {
            if (string.IsNullOrWhiteSpace(userRequest))
            {
                throw new ArgumentException("User request cannot be empty.", nameof(userRequest));
            }

            // 1. Session & task lifecycle
            var sessionData = await _sessionManager.LoadSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
            var agentTask = await _sessionManager.LoadOrCreateTaskAsync(taskId, sessionId, cancellationToken).ConfigureAwait(false);

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

            await _sessionManager.UpdateTaskStatusAsync(
                agentTask.Id, TaskState.Working, message: userMessage, final: false, cancellationToken).ConfigureAwait(false);

            // 2. Agent resolution & workflow execution
            var availableAgentCards = await _agentRegistry
                .GetAllAgentsAsync(cancellationToken)
                .ConfigureAwait(false);

            if (availableAgentCards.Count == 0)
            {
                _logger.LogWarning("No agents available to process request");
                var noAgentsMessage = "I don't have any specialized agents available right now. Please try again later.";
                await FailTaskAsync(agentTask, noAgentsMessage, cancellationToken).ConfigureAwait(false);
                return new OrchestratorResult { Text = noAgentsMessage };
            }

            var resolvedAgents = await _workflowFactory.ResolveAgentsAsync(availableAgentCards, cancellationToken).ConfigureAwait(false);
            var invokers = _workflowFactory.CreateInvokers(availableAgentCards, resolvedAgents);

            if (invokers.Count == 0)
            {
                _logger.LogWarning("Unable to build any agent invokers. Falling back to aggregator message.");
                var fallbackMessage = _aggregatorOptions.Value.DefaultFallbackMessage;
                await FailTaskAsync(agentTask, fallbackMessage, cancellationToken).ConfigureAwait(false);
                return new OrchestratorResult { Text = fallbackMessage };
            }

            var historyAwareRequest = SessionManager.BuildHistoryAwareRequest(sessionData, userRequest);

            // Initialize trace in the PARENT async context so AsyncLocal state
            // flows correctly into the workflow child contexts.
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

                await _observer.OnRequestStartedAsync(userRequest, historyMessages, cancellationToken).ConfigureAwait(false);
            }

            var workflowResult = await _workflowFactory.BuildAndExecuteAsync(
                invokers, historyAwareRequest, cancellationToken).ConfigureAwait(false);

            // 3. Post-processing
            var finalText = workflowResult?.Text ?? _aggregatorOptions.Value.DefaultFallbackMessage;
            var needsInput = workflowResult?.NeedsInput ?? false;

            if (needsInput)
            {
                _logger.LogInformation("Conversation requires user clarification for task {TaskId}", agentTask.Id);
            }

            if (_observer is not null)
            {
                await _observer.OnResponseAggregatedAsync(finalText, cancellationToken).ConfigureAwait(false);
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
            await _sessionManager.UpdateTaskStatusAsync(
                agentTask.Id, finalState, message: assistantMessage, final: !needsInput, CancellationToken.None).ConfigureAwait(false);

            _logger.LogInformation("Task {TaskId} ended with state {TaskState} and {HistoryCount} history items",
                agentTask.Id, finalState, agentTask.History.Count);

            await _sessionManager.SaveSessionAsync(
                sessionId, sessionData, userRequest, finalText, CancellationToken.None).ConfigureAwait(false);

            return new OrchestratorResult { Text = finalText, NeedsInput = needsInput };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing user request: {Request}", userRequest);

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
                        Parts = new List<Part> { new TextPart { Text = "I encountered an error while processing your request. Please try again." } }
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

            return new OrchestratorResult { Text = "I encountered an error while processing your request. Please try again." };
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
