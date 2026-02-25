using MongoDB.Bson.Serialization.Attributes;

namespace lucia.TimerAgent.ScheduledTasks;

/// <summary>
/// Persistence interface for alarm clock definitions and alarm sound catalog.
/// Alarm clocks are long-lived entities (recurring schedules), unlike one-shot scheduled tasks.
/// </summary>
public interface IAlarmClockRepository
{
    // -- Alarm Clock CRUD --

    Task<IReadOnlyList<AlarmClock>> GetAllAlarmsAsync(CancellationToken ct = default);
    Task<AlarmClock?> GetAlarmAsync(string alarmId, CancellationToken ct = default);
    Task UpsertAlarmAsync(AlarmClock alarm, CancellationToken ct = default);
    Task DeleteAlarmAsync(string alarmId, CancellationToken ct = default);

    /// <summary>
    /// Get all enabled alarms that have a NextFireAt in the past (need to be scheduled).
    /// </summary>
    Task<IReadOnlyList<AlarmClock>> GetDueAlarmsAsync(DateTimeOffset now, CancellationToken ct = default);

    // -- Alarm Sound catalog --

    Task<IReadOnlyList<AlarmSound>> GetAllSoundsAsync(CancellationToken ct = default);
    Task<AlarmSound?> GetSoundAsync(string soundId, CancellationToken ct = default);
    Task UpsertSoundAsync(AlarmSound sound, CancellationToken ct = default);
    Task DeleteSoundAsync(string soundId, CancellationToken ct = default);

    /// <summary>
    /// Get the default alarm sound. Returns null if no default is set.
    /// </summary>
    Task<AlarmSound?> GetDefaultSoundAsync(CancellationToken ct = default);
}
