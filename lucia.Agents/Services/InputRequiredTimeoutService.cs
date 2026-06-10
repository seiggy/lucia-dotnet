using A2A;
using lucia.Agents.Abstractions;
using lucia.Agents.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucia.Agents.Services;

/// <summary>
/// Background service that sweeps the task store for tasks stuck in
/// <see cref="TaskState.InputRequired"/> and auto-cancels them when no
/// input has been received within the configured <see cref="InputRequiredTimeoutOptions.Timeout"/>.
/// </summary>
/// <remarks>
/// Why a background sweeper instead of a per-task <see cref="CancellationTokenSource"/>?
/// Tasks are durable (Redis-backed) and survive process restarts; an in-memory CTS
/// would be lost on restart, leaving stale <c>InputRequired</c> tasks forever.
/// The sweeper re-reads the store on every interval so it naturally recovers
/// from restarts and is concurrency-safe by design.
/// </remarks>
public sealed class InputRequiredTimeoutService : BackgroundService
{
    private readonly ITaskIdIndex _taskIdIndex;
    private readonly ITaskStore _taskStore;
    private readonly TimeProvider _timeProvider;
    private readonly InputRequiredTimeoutOptions _options;
    private readonly ILogger<InputRequiredTimeoutService> _logger;

    public InputRequiredTimeoutService(
        ITaskIdIndex taskIdIndex,
        ITaskStore taskStore,
        TimeProvider timeProvider,
        IOptions<InputRequiredTimeoutOptions> options,
        ILogger<InputRequiredTimeoutService> logger)
    {
        _taskIdIndex = taskIdIndex ?? throw new ArgumentNullException(nameof(taskIdIndex));
        _taskStore = taskStore ?? throw new ArgumentNullException(nameof(taskStore));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "InputRequiredTimeoutService started. Timeout: {Timeout}, SweepInterval: {Interval}",
            _options.Timeout, _options.SweepInterval);

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
                _logger.LogError(ex, "InputRequired timeout sweep failed. Will retry next interval.");
            }
        }

        _logger.LogInformation("InputRequiredTimeoutService stopped.");
    }

    /// <summary>
    /// Single sweep pass: find all <c>InputRequired</c> tasks whose status timestamp
    /// is older than <see cref="InputRequiredTimeoutOptions.Timeout"/> and transition
    /// them to <c>Canceled</c>.
    /// </summary>
    /// <remarks>
    /// Idempotency: only tasks still in <c>InputRequired</c> at the moment of the
    /// cancel write are transitioned — a double-check re-read guards against a
    /// concurrent input arriving between the initial read and the write.  If the
    /// same task is encountered on a subsequent sweep (after it has already been
    /// cancelled or otherwise resolved), the <c>InputRequired</c> guard short-circuits
    /// the loop body, so no double-cancel or exception occurs.
    /// </remarks>
    internal async Task SweepAsync(CancellationToken cancellationToken)
    {
        var taskIds = await _taskIdIndex
            .GetAllTrackedTaskIdsAsync(cancellationToken)
            .ConfigureAwait(false);

        if (taskIds.Count == 0)
            return;

        var now = _timeProvider.GetUtcNow();
        int inputRequiredSeen = 0;
        int cancelledCount = 0;

        foreach (var taskId in taskIds)
        {
            try
            {
                var task = await _taskStore.GetTaskAsync(taskId, cancellationToken).ConfigureAwait(false);

                // Skip tasks that are not in InputRequired or have no status timestamp
                if (task is null || task.Status.State != TaskState.InputRequired)
                    continue;

                inputRequiredSeen++;

                // Timestamp is nullable; skip tasks with no recorded entry time.
                if (task.Status.Timestamp is not { } enteredAt)
                    continue;

                var elapsed = now - enteredAt;
                if (elapsed < _options.Timeout)
                    continue;

                // Double-check: re-read the freshest state to guard against a concurrent
                // input submission arriving between the initial read and our write.
                var freshTask = await _taskStore.GetTaskAsync(taskId, cancellationToken).ConfigureAwait(false);
                if (freshTask is null || freshTask.Status.State != TaskState.InputRequired)
                    continue;

                var cancelMessage = new Message
                {
                    Role = Role.Agent,
                    MessageId = Guid.NewGuid().ToString("N"),
                    TaskId = taskId,
                    ContextId = freshTask.ContextId,
                    Parts = [new Part { Text = "No input was received within the allowed time. The pending request has been cancelled." }]
                };

                freshTask.History ??= [];
                freshTask.History.Add(cancelMessage);
                freshTask.Status = new A2A.TaskStatus
                {
                    State = TaskState.Canceled,
                    Message = cancelMessage,
                    Timestamp = _timeProvider.GetUtcNow()
                };

                await _taskStore.SaveTaskAsync(taskId, freshTask, cancellationToken).ConfigureAwait(false);

                cancelledCount++;
                _logger.TaskAutoCancel(taskId, (long)elapsed.TotalSeconds, _options.Timeout);
            }
            catch (OperationCanceledException)
            {
                // Shutdown cancellation — let it propagate so ExecuteAsync exits cleanly
                // without emitting a per-task "failed to process" warning.
                throw;
            }
            catch (Exception ex)
            {
                _logger.TaskCheckFailed(ex, taskId);
            }
        }

        if (cancelledCount > 0)
        {
            _logger.SweepCompleted(cancelledCount, inputRequiredSeen);
        }
    }
}
