namespace lucia.TimerAgent.ScheduledTasks;

/// <summary>
/// Persistence interface for scheduled task documents.
/// Backed by MongoDB for durable storage across restarts.
/// </summary>
public interface IScheduledTaskRepository
{
    /// <summary>
    /// Persists a task document (upsert by Id).
    /// </summary>
    Task UpsertAsync(ScheduledTaskDocument document, CancellationToken ct = default);

    /// <summary>
    /// Retrieves a task document by its ID.
    /// </summary>
    Task<ScheduledTaskDocument?> GetByIdAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Returns all tasks in a recoverable state (Pending or Active).
    /// Used by <see cref="ScheduledTaskRecoveryService"/> at startup.
    /// </summary>
    Task<IReadOnlyList<ScheduledTaskDocument>> GetRecoverableTasksAsync(CancellationToken ct = default);

    /// <summary>
    /// Updates only the status field of a task document.
    /// </summary>
    Task UpdateStatusAsync(string id, ScheduledTaskStatus status, CancellationToken ct = default);

    /// <summary>
    /// Deletes a task document by its ID.
    /// </summary>
    Task DeleteAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Deletes all completed one-shot tasks older than the specified age.
    /// Called periodically to clean up expired entries.
    /// </summary>
    Task<long> PurgeCompletedAsync(TimeSpan olderThan, CancellationToken ct = default);
}
