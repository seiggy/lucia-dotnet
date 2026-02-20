using A2A;
using lucia.Agents.Training.Models;

namespace lucia.Agents.Services;

/// <summary>
/// Durable archive store for completed agent tasks (MongoDB-backed).
/// </summary>
public interface ITaskArchiveStore
{
    /// <summary>
    /// Archive a task that has reached a terminal state.
    /// Upserts by task ID to handle duplicate archival attempts.
    /// </summary>
    Task ArchiveTaskAsync(AgentTask task, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieve a single archived task by ID.
    /// </summary>
    Task<ArchivedTask?> GetArchivedTaskAsync(string taskId, CancellationToken cancellationToken = default);

    /// <summary>
    /// List archived tasks with filtering and pagination.
    /// </summary>
    Task<PagedResult<ArchivedTask>> ListArchivedTasksAsync(TaskFilterCriteria filter, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get aggregate statistics about archived tasks.
    /// </summary>
    Task<TaskStats> GetTaskStatsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a task has already been archived.
    /// </summary>
    Task<bool> IsArchivedAsync(string taskId, CancellationToken cancellationToken = default);
}
