using A2A;
using lucia.Agents.Abstractions;
using lucia.Agents.Models;
using lucia.Agents.Services;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.Orchestration;

/// <summary>
/// Manages session lifecycle and task persistence for the orchestrator.
/// Handles loading/saving sessions from the Redis cache and creating/updating
/// durable agent tasks via the A2A task store.
/// </summary>
public sealed class SessionManager
{
    private readonly ISessionCacheService? _sessionCache;
    private readonly ITaskStore _taskStore;
    private readonly ILogger<SessionManager> _logger;

    public SessionManager(
        ITaskStore taskStore,
        ILogger<SessionManager> logger,
        ISessionCacheService? sessionCache = null)
    {
        _taskStore = taskStore ?? throw new ArgumentNullException(nameof(taskStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _sessionCache = sessionCache;
    }

    /// <summary>
    /// Loads an existing session from the cache, or returns null if not found.
    /// </summary>
    public async Task<SessionData?> LoadSessionAsync(string? sessionId, CancellationToken cancellationToken)
    {
        if (_sessionCache is null || string.IsNullOrWhiteSpace(sessionId))
            return null;

        var sessionData = await _sessionCache.GetSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
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

        return sessionData;
    }

    /// <summary>
    /// Loads an existing agent task or creates a new one for durable persistence.
    /// </summary>
    public async Task<AgentTask> LoadOrCreateTaskAsync(
        string? taskId, string? sessionId, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(taskId))
        {
            var existingTask = await _taskStore.GetTaskAsync(taskId, cancellationToken).ConfigureAwait(false);

            if (existingTask is not null)
            {
                _logger.LogInformation("Loaded existing task {TaskId} with {HistoryCount} history items",
                    existingTask.Id, existingTask.History?.Count ?? 0);
                return existingTask;
            }

            _logger.LogWarning("Task {TaskId} not found, creating new task", taskId);
        }

        var newTaskId = taskId ?? Guid.NewGuid().ToString("N");
        var agentTask = new AgentTask
        {
            Id = newTaskId,
            ContextId = sessionId ?? Guid.NewGuid().ToString("N"),
            Status = new A2A.TaskStatus { State = TaskState.Submitted, Timestamp = DateTimeOffset.UtcNow }
        };
        await _taskStore.SaveTaskAsync(newTaskId, agentTask, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Created new task {TaskId} with context {ContextId}",
            agentTask.Id, agentTask.ContextId);
        return agentTask;
    }

    /// <summary>
    /// Updates task status and persists the change to the task store.
    /// </summary>
    public async Task UpdateTaskStatusAsync(
        string taskId,
        TaskState state,
        Message? message,
        bool final,
        CancellationToken cancellationToken)
    {
        try
        {
            var task = await _taskStore.GetTaskAsync(taskId, cancellationToken).ConfigureAwait(false);
            if (task is null)
            {
                _logger.LogWarning("Cannot update status for task {TaskId}: not found.", taskId);
                return;
            }

            if (message is not null)
            {
                task.History ??= [];
                task.History.Add(message);
            }

            task.Status = new A2A.TaskStatus
            {
                State = state,
                Message = message,
                Timestamp = DateTimeOffset.UtcNow
            };

            await _taskStore.SaveTaskAsync(taskId, task, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error attempting to update the task status.");
        }
    }

    /// <summary>
    /// Persists session data with user/assistant turns to the Redis cache
    /// for multi-turn conversation continuity.
    /// </summary>
    public async Task SaveSessionAsync(
        string? sessionId,
        SessionData? sessionData,
        string userRequest,
        string response,
        CancellationToken cancellationToken)
    {
        if (_sessionCache is null || string.IsNullOrWhiteSpace(sessionId))
            return;

        sessionData ??= new SessionData { SessionId = sessionId };
        sessionData.History.Add(new SessionTurn { Role = "user", Content = userRequest });
        sessionData.History.Add(new SessionTurn { Role = "assistant", Content = response });
        await _sessionCache.SaveSessionAsync(sessionData, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Prepends conversation history to the user request so the router and agents
    /// have multi-turn context. Returns the original request if no history exists.
    /// </summary>
    public static string BuildHistoryAwareRequest(SessionData? sessionData, string currentRequest)
    {
        if (sessionData is null || sessionData.History.Count == 0)
            return currentRequest;

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
