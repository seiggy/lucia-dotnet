using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace lucia.TimerAgent.ScheduledTasks;

/// <summary>
/// Background service that polls the <see cref="ScheduledTaskStore"/> every second
/// and fires tasks whose <see cref="IScheduledTask.FireAt"/> has elapsed.
/// Each task type defines its own execution logic via <see cref="IScheduledTask.ExecuteAsync"/>.
/// </summary>
public sealed class ScheduledTaskService : BackgroundService
{
    private static readonly ActivitySource ActivitySource = new("Lucia.ScheduledTasks", "1.0.0");

    private readonly ScheduledTaskStore _store;
    private readonly IServiceProvider _serviceProvider;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ScheduledTaskService> _logger;

    public ScheduledTaskService(
        ScheduledTaskStore store,
        IServiceProvider serviceProvider,
        TimeProvider timeProvider,
        ILogger<ScheduledTaskService> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ScheduledTaskService started — polling for expired tasks");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessExpiredTasksAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Unexpected error in scheduled task execution loop");
            }

            await Task.Delay(TimeSpan.FromSeconds(1), _timeProvider, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task ProcessExpiredTasksAsync(CancellationToken stoppingToken)
    {
        var now = _timeProvider.GetUtcNow();
        var tasks = _store.GetAll();

        foreach (var task in tasks)
        {
            if (stoppingToken.IsCancellationRequested)
                break;

            if (!task.IsExpired(now))
                continue;

            // Remove from store before firing to prevent double-execution
            if (!_store.TryRemove(task.Id, out _))
                continue;

            // Fire-and-forget with structured logging and telemetry
            _ = FireTaskAsync(task, stoppingToken);
        }
    }

    private async Task FireTaskAsync(IScheduledTask task, CancellationToken stoppingToken)
    {
        using var activity = ActivitySource.StartActivity("ScheduledTask.Fire", ActivityKind.Internal);
        activity?.SetTag("scheduled_task.id", task.Id);
        activity?.SetTag("scheduled_task.task_id", task.TaskId);
        activity?.SetTag("scheduled_task.type", task.TaskType.ToString());
        activity?.SetTag("scheduled_task.label", task.Label);

        try
        {
            _logger.LogInformation(
                "Firing scheduled task {TaskId} ({TaskType}): {Label}",
                task.Id, task.TaskType, task.Label);

            await task.ExecuteAsync(_serviceProvider, stoppingToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Scheduled task {TaskId} ({TaskType}) completed successfully",
                task.Id, task.TaskType);

            // Persist completion status
            await UpdateTaskStatusAsync(task, ScheduledTaskStatus.Completed, stoppingToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "Scheduled task {TaskId} ({TaskType}) failed: {Error}",
                task.Id, task.TaskType, ex.Message);

            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);

            await UpdateTaskStatusAsync(task, ScheduledTaskStatus.Failed, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task UpdateTaskStatusAsync(
        IScheduledTask task,
        ScheduledTaskStatus status,
        CancellationToken stoppingToken)
    {
        try
        {
            var repo = _serviceProvider.GetService<IScheduledTaskRepository>();
            if (repo is not null)
            {
                await repo.UpdateStatusAsync(task.Id, status, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to update task {TaskId} status to {Status}",
                task.Id, status);
        }
    }
}
