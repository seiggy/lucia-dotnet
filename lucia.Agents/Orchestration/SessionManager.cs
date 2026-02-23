using A2A;
using lucia.Agents.Services;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.Orchestration;

/// <summary>
/// Manages session lifecycle and task persistence for the orchestrator.
/// Handles loading/saving sessions from the Redis cache and creating/updating
/// durable agent tasks via the A2A task manager.
/// </summary>
public sealed class SessionManager
{
    private readonly ISessionCacheService? _sessionCache;
    private readonly ITaskManager _taskManager;
    private readonly ILogger<SessionManager> _logger;

    public SessionManager(
        ITaskManager taskManager,
        ILogger<SessionManager> logger,
        ISessionCacheService? sessionCache = null)
    {
        _taskManager = taskManager ?? throw new ArgumentNullException(nameof(taskManager));
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
            var taskQueryParams = new TaskQueryParams { Id = taskId };
            var existingTask = await _taskManager.GetTaskAsync(taskQueryParams, cancellationToken).ConfigureAwait(false);

            if (existingTask is not null)
            {
                _logger.LogInformation("Loaded existing task {TaskId} with {HistoryCount} history items",
                    existingTask.Id, existingTask.History?.Count ?? 0);
                return existingTask;
            }

            _logger.LogWarning("Task {TaskId} not found, creating new task", taskId);
            return await _taskManager.CreateTaskAsync(sessionId, taskId, cancellationToken).ConfigureAwait(false);
        }

        var agentTask = await _taskManager.CreateTaskAsync(sessionId, taskId: null, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Created new task {TaskId} with context {ContextId}",
            agentTask.Id, agentTask.ContextId);
        return agentTask;
    }

    /// <summary>
    /// Delegates task status update to the underlying task manager.
    /// </summary>
    public Task UpdateTaskStatusAsync(
        string taskId,
        TaskState state,
        AgentMessage? message,
        bool final,
        CancellationToken cancellationToken)
    {
        return _taskManager.UpdateStatusAsync(taskId, state, message, final, cancellationToken);
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
