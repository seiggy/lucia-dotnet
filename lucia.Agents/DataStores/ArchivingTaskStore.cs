using A2A;
using lucia.Agents.Abstractions;
using lucia.Agents.Services;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.DataStores;

/// <summary>
/// Decorator around <see cref="ITaskStore"/> that archives tasks to MongoDB
/// when they reach a terminal state (Completed, Failed, Canceled).
/// </summary>
public sealed class ArchivingTaskStore : ITaskStore
{
    private readonly ITaskStore _inner;
    private readonly ITaskArchiveStore _archive;
    private readonly ILogger<ArchivingTaskStore> _logger;

    private static readonly HashSet<TaskState> TerminalStates =
    [
        TaskState.Completed,
        TaskState.Failed,
        TaskState.Canceled,
    ];

    public ArchivingTaskStore(
        ITaskStore inner,
        ITaskArchiveStore archive,
        ILogger<ArchivingTaskStore> logger)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _archive = archive ?? throw new ArgumentNullException(nameof(archive));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<AgentTask?> GetTaskAsync(string taskId, CancellationToken cancellationToken = default)
        => _inner.GetTaskAsync(taskId, cancellationToken);

    public async Task SaveTaskAsync(string taskId, AgentTask task, CancellationToken cancellationToken = default)
    {
        await _inner.SaveTaskAsync(taskId, task, cancellationToken).ConfigureAwait(false);

        if (TerminalStates.Contains(task.Status.State))
        {
            await TryArchiveAsync(taskId, cancellationToken).ConfigureAwait(false);
        }
    }

    public Task DeleteTaskAsync(string taskId, CancellationToken cancellationToken = default)
        => _inner.DeleteTaskAsync(taskId, cancellationToken);

    public Task<ListTasksResponse> ListTasksAsync(ListTasksRequest request, CancellationToken cancellationToken = default)
        => _inner.ListTasksAsync(request, cancellationToken);

    private async Task TryArchiveAsync(string taskId, CancellationToken cancellationToken)
    {
        try
        {
            var task = await _inner.GetTaskAsync(taskId, cancellationToken).ConfigureAwait(false);
            if (task is null)
            {
                _logger.LogWarning("Cannot archive task {TaskId}: not found in primary store.", taskId);
                return;
            }

            await _archive.ArchiveTaskAsync(task, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Archived task {TaskId} with status {Status}.", taskId, task.Status.State);
        }
        catch (Exception ex)
        {
            // Fire-and-forget: archive failure should not break the primary flow
            _logger.LogError(ex, "Failed to archive task {TaskId} to MongoDB.", taskId);
        }
    }
}
