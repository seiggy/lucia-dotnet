using A2A;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace lucia.TimerAgent;

/// <summary>
/// Background service that recovers persisted timer tasks from Redis on startup.
/// Scans for AgentTask entries with timer metadata in Working state and resumes them.
/// </summary>
public sealed class TimerRecoveryService : BackgroundService
{
    private const string TaskIdSetKey = "lucia:task-ids";
    
    private readonly IConnectionMultiplexer _redis;
    private readonly ITaskStore _taskStore;
    private readonly TimerSkill _timerSkill;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<TimerRecoveryService> _logger;

    public TimerRecoveryService(
        IConnectionMultiplexer redis,
        ITaskStore taskStore,
        TimerSkill timerSkill,
        TimeProvider timeProvider,
        ILogger<TimerRecoveryService> logger)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _taskStore = taskStore ?? throw new ArgumentNullException(nameof(taskStore));
        _timerSkill = timerSkill ?? throw new ArgumentNullException(nameof(timerSkill));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Brief delay to let the app finish starting up
        await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken).ConfigureAwait(false);

        _logger.LogInformation("TimerRecoveryService starting — scanning for persisted timer tasks");

        try
        {
            var recovered = 0;
            var expired = 0;
            var db = _redis.GetDatabase();
            var taskIdValues = await db.SetMembersAsync(TaskIdSetKey);
            var taskIds = taskIdValues
                .Where(v => v.HasValue)
                .Select(v => v.ToString())
                .ToList();

            foreach (var taskId in taskIds)
            {
                if (stoppingToken.IsCancellationRequested)
                    break;

                var task = await _taskStore.GetTaskAsync(taskId, stoppingToken).ConfigureAwait(false);
                if (task is null)
                    continue;

                // Only recover tasks in Working state that have timer metadata
                if (task.Status.State != TaskState.Working)
                    continue;

                if (task.Metadata is null ||
                    !task.Metadata.TryGetValue("timer.agentId", out var agentIdElement) ||
                    agentIdElement.GetString() != "timer-agent")
                    continue;

                // Extract timer metadata
                if (!task.Metadata.TryGetValue("timer.timerId", out var timerIdEl) ||
                    !task.Metadata.TryGetValue("timer.message", out var messageEl) ||
                    !task.Metadata.TryGetValue("timer.entityId", out var entityIdEl) ||
                    !task.Metadata.TryGetValue("timer.durationSeconds", out var durationEl) ||
                    !task.Metadata.TryGetValue("timer.expiresAtUtc", out var expiresEl))
                {
                    _logger.LogWarning("Timer task {TaskId} has incomplete metadata, skipping", taskId);
                    continue;
                }

                var timerId = timerIdEl.GetString()!;
                var message = messageEl.GetString()!;
                var entityId = entityIdEl.GetString()!;
                var durationSeconds = durationEl.GetInt32();
                var expiresAt = DateTimeOffset.Parse(expiresEl.GetString()!);

                var remaining = expiresAt - _timeProvider.GetUtcNow();

                if (remaining <= TimeSpan.FromSeconds(-300))
                {
                    // Timer expired more than 5 minutes ago — mark as failed (missed)
                    _logger.LogWarning(
                        "Timer {TimerId} (task {TaskId}) expired {Ago} ago, marking as failed",
                        timerId, taskId, -remaining);

                    try
                    {
                        var missedMessage = new AgentMessage
                        {
                            MessageId = Guid.NewGuid().ToString("N"),
                            Role = MessageRole.Agent,
                            Parts = [new TextPart { Text = $"Timer missed — expired {FormatDuration(-remaining)} ago during downtime" }]
                        };
                        await _taskStore.UpdateStatusAsync(taskId, TaskState.Failed, missedMessage, stoppingToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to mark expired timer task {TaskId} as failed", taskId);
                    }

                    expired++;
                    continue;
                }

                // Resume the timer (will fire immediately if already past expiry within the 5-min grace window)
                _timerSkill.ResumeTimer(timerId, taskId, message, entityId, durationSeconds, expiresAt);
                recovered++;
            }

            _logger.LogInformation(
                "TimerRecoveryService complete — recovered {Recovered} timer(s), {Expired} expired",
                recovered, expired);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "TimerRecoveryService failed during recovery scan");
        }
    }

    private static string FormatDuration(TimeSpan ts)
    {
        if (ts.TotalHours >= 1)
            return $"{(int)ts.TotalHours}h {ts.Minutes}m";
        if (ts.TotalMinutes >= 1)
            return $"{(int)ts.TotalMinutes}m {ts.Seconds}s";
        return $"{(int)ts.TotalSeconds}s";
    }
}
