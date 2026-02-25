using Cronos;
using Microsoft.Extensions.Logging;

namespace lucia.TimerAgent.ScheduledTasks;

/// <summary>
/// Computes next fire times from CRON expressions and advances alarm schedules after firing.
/// Uses the Cronos library with standard 5-field CRON format (minute, hour, day, month, weekday).
/// </summary>
public sealed class CronScheduleService
{
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CronScheduleService> _logger;

    public CronScheduleService(TimeProvider timeProvider, ILogger<CronScheduleService> logger)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Computes the next occurrence from a CRON expression, relative to the current time.
    /// Returns null if the expression is invalid or has no future occurrence.
    /// </summary>
    public DateTimeOffset? GetNextOccurrence(string cronExpression)
    {
        return GetNextOccurrence(cronExpression, _timeProvider.GetUtcNow());
    }

    /// <summary>
    /// Computes the next occurrence from a CRON expression, relative to a given time.
    /// Returns null if the expression is invalid or has no future occurrence.
    /// </summary>
    public DateTimeOffset? GetNextOccurrence(string cronExpression, DateTimeOffset from)
    {
        if (!TryParse(cronExpression, out var cron))
        {
            _logger.LogWarning("Invalid CRON expression: {CronExpression}", cronExpression);
            return null;
        }

        var next = cron!.GetNextOccurrence(from.UtcDateTime, inclusive: false);
        return next.HasValue ? new DateTimeOffset(next.Value, TimeSpan.Zero) : null;
    }

    /// <summary>
    /// Validates a CRON expression string.
    /// </summary>
    public bool IsValid(string cronExpression)
    {
        return TryParse(cronExpression, out _);
    }

    /// <summary>
    /// Returns a human-readable description of a CRON schedule for display purposes.
    /// </summary>
    public static string Describe(string cronExpression)
    {
        if (!TryParse(cronExpression, out var cron))
            return "Invalid schedule";

        // Parse fields for readable description
        var parts = cronExpression.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 5)
            return cronExpression;

        var minute = parts[0];
        var hour = parts[1];
        var dayOfWeek = parts[4];

        var timeStr = (int.TryParse(hour, out var h) && int.TryParse(minute, out var m))
            ? new TimeOnly(h, m).ToString("h:mm tt")
            : $"{hour}:{minute}";

        return dayOfWeek switch
        {
            "*" => $"Daily at {timeStr}",
            "1-5" => $"Weekdays at {timeStr}",
            "0,6" or "6,0" => $"Weekends at {timeStr}",
            _ => $"{timeStr} on days {dayOfWeek}"
        };
    }

    /// <summary>
    /// Advances an alarm clock's <see cref="AlarmClock.NextFireAt"/> to the next CRON occurrence.
    /// For one-shot alarms (no CronSchedule), sets NextFireAt to null and disables the alarm.
    /// Returns true if the alarm was advanced (still active), false if it became inactive.
    /// </summary>
    public bool AdvanceSchedule(AlarmClock alarm)
    {
        if (string.IsNullOrWhiteSpace(alarm.CronSchedule))
        {
            // One-shot alarm — deactivate after firing
            alarm.NextFireAt = null;
            alarm.IsEnabled = false;
            _logger.LogInformation("One-shot alarm {AlarmId} fired and deactivated", alarm.Id);
            return false;
        }

        var next = GetNextOccurrence(alarm.CronSchedule);
        if (next is null)
        {
            _logger.LogWarning(
                "Alarm {AlarmId} has CRON '{CronSchedule}' but no future occurrence found — deactivating",
                alarm.Id, alarm.CronSchedule);
            alarm.NextFireAt = null;
            alarm.IsEnabled = false;
            return false;
        }

        alarm.NextFireAt = next;
        _logger.LogInformation(
            "Alarm {AlarmId} advanced to next occurrence: {NextFireAt}",
            alarm.Id, next);
        return true;
    }

    /// <summary>
    /// Initializes <see cref="AlarmClock.NextFireAt"/> from its CRON schedule if not already set.
    /// Call this when creating or enabling a CRON-based alarm.
    /// </summary>
    public void InitializeNextFireAt(AlarmClock alarm)
    {
        if (string.IsNullOrWhiteSpace(alarm.CronSchedule))
            return;

        alarm.NextFireAt = GetNextOccurrence(alarm.CronSchedule);
    }

    private static bool TryParse(string cronExpression, out CronExpression? result)
    {
        try
        {
            result = CronExpression.Parse(cronExpression, CronFormat.Standard);
            return true;
        }
        catch (CronFormatException)
        {
            result = null;
            return false;
        }
    }
}
