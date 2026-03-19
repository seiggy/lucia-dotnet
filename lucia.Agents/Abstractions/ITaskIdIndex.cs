namespace lucia.Agents.Abstractions;

/// <summary>
/// Provides enumeration of tracked task IDs for archival and maintenance operations.
/// Implemented by task store implementations that maintain an index of known task IDs.
/// </summary>
public interface ITaskIdIndex
{
    /// <summary>
    /// Returns all tracked task IDs. Used by archival services to sweep for terminal-state tasks.
    /// </summary>
    Task<IReadOnlyList<string>> GetAllTrackedTaskIdsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a task ID from the tracking index. Called when a task has been fully processed.
    /// </summary>
    Task RemoveTaskIdAsync(string taskId, CancellationToken cancellationToken = default);
}
