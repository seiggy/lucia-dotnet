using A2A;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace lucia.Agents.Services;

/// <summary>
/// Background service that periodically sweeps Redis for tasks in terminal state
/// and archives them to MongoDB. Acts as a safety net for the <see cref="ArchivingTaskStore"/> decorator.
/// </summary>
public sealed class TaskArchivalService : BackgroundService
{
    private readonly IConnectionMultiplexer _redis;
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

    private const string TaskIdSetKey = "lucia:task-ids";

    public TaskArchivalService(
        IConnectionMultiplexer redis,
        ITaskStore taskStore,
        ITaskArchiveStore archive,
        ILogger<TaskArchivalService> logger,
        IOptions<TaskArchiveOptions> options)
    {
        _redis = redis;
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
                await Task.Delay(_options.SweepInterval, stoppingToken);
                await SweepAsync(stoppingToken);
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
        var db = _redis.GetDatabase();
        var taskIdValues = await db.SetMembersAsync(TaskIdSetKey);

        var taskIds = taskIdValues
            .Where(v => v.HasValue)
            .Select(v => v.ToString())
            .ToList();

        if (taskIds.Count == 0)
        {
            return;
        }

        int archivedCount = 0;

        foreach (var taskId in taskIds)
        {
            try
            {
                // Skip if already archived
                if (await _archive.IsArchivedAsync(taskId, cancellationToken))
                {
                    continue;
                }

                var task = await _taskStore.GetTaskAsync(taskId, cancellationToken);
                if (task is null)
                {
                    // Task expired from Redis but ID remains in the set — clean up
                    _ = db.SetRemoveAsync(TaskIdSetKey, taskId, CommandFlags.FireAndForget);
                    continue;
                }

                var state = task.Status.State;
                if (TerminalStates.Contains(state))
                {
                    await _archive.ArchiveTaskAsync(task, cancellationToken);
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
