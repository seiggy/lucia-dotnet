using A2A;
using lucia.Agents.Abstractions;
using lucia.Agents.Configuration;
using lucia.Agents.DataStores;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucia.Agents.Services;

/// <summary>
/// Background service that periodically sweeps for tasks in terminal state
/// and archives them to durable storage. Acts as a safety net for the <see cref="ArchivingTaskStore"/> decorator.
/// </summary>
public sealed class TaskArchivalService : BackgroundService
{
    private readonly ITaskIdIndex _taskIdIndex;
    private readonly ITaskStore _taskStore;
    private readonly ITaskArchiveStore _archive;
    private readonly ILogger<TaskArchivalService> _logger;
    private readonly TaskArchiveOptions _options;

    private static readonly HashSet<TaskState> TerminalStates =
    [
        TaskState.Completed,
        TaskState.Failed,
        TaskState.Canceled,
    ];

    public TaskArchivalService(
        ITaskIdIndex taskIdIndex,
        ITaskStore taskStore,
        ITaskArchiveStore archive,
        ILogger<TaskArchivalService> logger,
        IOptions<TaskArchiveOptions> options)
    {
        _taskIdIndex = taskIdIndex;
        _taskStore = taskStore;
        _archive = archive;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TaskArchivalService started. Sweep interval: {Interval}", _options.SweepInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_options.SweepInterval, stoppingToken).ConfigureAwait(false);
                await SweepAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Task archival sweep failed. Will retry next interval.");
            }
        }

        _logger.LogInformation("TaskArchivalService stopped.");
    }

    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        var taskIds = await _taskIdIndex.GetAllTrackedTaskIdsAsync(cancellationToken).ConfigureAwait(false);

        if (taskIds.Count == 0)
        {
            return;
        }

        int archivedCount = 0;

        foreach (var taskId in taskIds)
        {
            try
            {
                if (await _archive.IsArchivedAsync(taskId, cancellationToken).ConfigureAwait(false))
                {
                    continue;
                }

                var task = await _taskStore.GetTaskAsync(taskId, cancellationToken).ConfigureAwait(false);
                if (task is null)
                {
                    await _taskIdIndex.RemoveTaskIdAsync(taskId, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var state = task.Status.State;
                if (TerminalStates.Contains(state))
                {
                    await _archive.ArchiveTaskAsync(task, cancellationToken).ConfigureAwait(false);
                    archivedCount++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to archive task {TaskId} during sweep.", taskId);
            }
        }

        if (archivedCount > 0)
        {
            _logger.LogInformation("Archival sweep completed: archived {Count} tasks from {Total} task IDs.",
                archivedCount, taskIds.Count);
        }
    }
}
