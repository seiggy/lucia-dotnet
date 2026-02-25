using System.Diagnostics;
using lucia.Agents.Services;
using lucia.TimerAgent.ScheduledTasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace lucia.TimerAgent;

/// <summary>
/// Skill that manages alarm clocks — recurring or one-shot alarms that ring on media_player devices.
/// Alarms are persisted in MongoDB via <see cref="IAlarmClockRepository"/> and executed as
/// <see cref="AlarmScheduledTask"/> instances through the <see cref="ScheduledTaskStore"/> pipeline.
/// </summary>
public sealed class AlarmSkill
{
    private static readonly ActivitySource ActivitySource = new("Lucia.Skills.Alarm", "1.0.0");

    private readonly IAlarmClockRepository _alarmRepository;
    private readonly ScheduledTaskStore _taskStore;
    private readonly IScheduledTaskRepository _taskRepository;
    private readonly CronScheduleService _cronService;
    private readonly IEntityLocationService _entityLocationService;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AlarmSkill> _logger;

    public AlarmSkill(
        IAlarmClockRepository alarmRepository,
        ScheduledTaskStore taskStore,
        IScheduledTaskRepository taskRepository,
        CronScheduleService cronService,
        IEntityLocationService entityLocationService,
        TimeProvider timeProvider,
        ILogger<AlarmSkill> logger)
    {
        _alarmRepository = alarmRepository ?? throw new ArgumentNullException(nameof(alarmRepository));
        _taskStore = taskStore ?? throw new ArgumentNullException(nameof(taskStore));
        _taskRepository = taskRepository ?? throw new ArgumentNullException(nameof(taskRepository));
        _cronService = cronService ?? throw new ArgumentNullException(nameof(cronService));
        _entityLocationService = entityLocationService ?? throw new ArgumentNullException(nameof(entityLocationService));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Returns the AI tool definitions for the alarm skill.
    /// </summary>
    public IList<AITool> GetTools()
    {
        return
        [
            AIFunctionFactory.Create(SetAlarmAsync, new AIFunctionFactoryOptions
            {
                Name = "SetAlarm",
                Description = """
                    Creates an alarm that will ring on a media player device at the specified time.
                    Use this when the user asks to set an alarm, wake-up alarm, or scheduled alert.

                    Parameters:
                    - name: A friendly name for the alarm (e.g., "Morning Wake Up", "Nap Alarm").
                    - time: The time for the alarm in 24-hour HH:mm format (e.g., "07:00", "14:30").
                    - location: The room/area or media_player entity_id where the alarm should play
                      (e.g., "bedroom", "media_player.bedroom_speaker"). Use "presence" to play
                      on whichever speaker detects presence at alarm time.
                    - cronSchedule: Optional CRON expression for recurring alarms (e.g., "0 7 * * 1-5"
                      for weekdays at 7 AM). Omit for a one-shot alarm that fires once at the given time.
                    - soundName: Optional alarm sound name (e.g., "Gentle", "Radar"). If omitted,
                      uses the default alarm sound or falls back to TTS announcement.
                    """
            }),
            AIFunctionFactory.Create(DismissAlarmAsync, new AIFunctionFactoryOptions
            {
                Name = "DismissAlarm",
                Description = """
                    Dismisses a currently ringing alarm or disables a scheduled alarm.
                    Use when the user says "dismiss alarm", "stop alarm", "turn off alarm", etc.
                    The alarmId can be the alarm ID or the alarm name.
                    """
            }),
            AIFunctionFactory.Create(SnoozeAlarmAsync, new AIFunctionFactoryOptions
            {
                Name = "SnoozeAlarm",
                Description = """
                    Snoozes a currently ringing alarm for a specified number of minutes.
                    The alarm will stop ringing now and fire again after the snooze period.
                    Default snooze duration is 9 minutes if not specified.
                    The alarmId can be the alarm ID or the alarm name.
                    """
            }),
            AIFunctionFactory.Create(ListAlarmsAsync, new AIFunctionFactoryOptions
            {
                Name = "ListAlarms",
                Description = "Lists all configured alarms with their schedule, status, and next fire time."
            })
        ];
    }

    /// <summary>
    /// Creates an alarm clock definition and schedules it for execution.
    /// </summary>
    public async Task<string> SetAlarmAsync(
        string name,
        string time,
        string location,
        string? cronSchedule = null,
        string? soundName = null)
    {
        using var activity = ActivitySource.StartActivity("AlarmSkill.SetAlarm", ActivityKind.Internal);

        if (string.IsNullOrWhiteSpace(name))
            return "Alarm name is required.";

        if (string.IsNullOrWhiteSpace(location))
            return "Location or media_player entity is required.";

        // Resolve target entity
        var targetEntity = await ResolveMediaPlayerEntityAsync(location).ConfigureAwait(false);
        if (targetEntity is null)
            return $"Could not find a media player device in '{location}'.";

        // Resolve alarm sound
        string? alarmSoundId = null;
        if (!string.IsNullOrWhiteSpace(soundName))
        {
            var sounds = await _alarmRepository.GetAllSoundsAsync().ConfigureAwait(false);
            var match = sounds.FirstOrDefault(s =>
                s.Name.Equals(soundName, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                var available = sounds.Count > 0
                    ? string.Join(", ", sounds.Select(s => s.Name))
                    : "none configured";
                return $"Alarm sound '{soundName}' not found. Available sounds: {available}.";
            }
            alarmSoundId = match.Id;
        }

        // Build CRON expression from time + optional recurrence
        string? effectiveCron = null;
        DateTimeOffset? nextFireAt = null;

        if (!string.IsNullOrWhiteSpace(cronSchedule))
        {
            if (!_cronService.IsValid(cronSchedule))
                return $"Invalid CRON schedule: '{cronSchedule}'. Use standard 5-field format (minute hour day month weekday).";

            effectiveCron = cronSchedule;
        }
        else if (TryParseTime(time, out var hour, out var minute))
        {
            // One-shot alarm — compute next occurrence of this time
            nextFireAt = ComputeNextFireAt(hour, minute);
        }
        else
        {
            return $"Invalid time format '{time}'. Use 24-hour HH:mm format (e.g., '07:00', '14:30').";
        }

        var alarmId = Guid.NewGuid().ToString("N")[..8];
        var alarm = new AlarmClock
        {
            Id = alarmId,
            Name = name,
            TargetEntity = targetEntity,
            AlarmSoundId = alarmSoundId,
            CronSchedule = effectiveCron,
            NextFireAt = nextFireAt
        };

        // For CRON alarms, compute the first NextFireAt
        if (effectiveCron is not null)
        {
            _cronService.InitializeNextFireAt(alarm);
        }

        activity?.SetTag("alarm.id", alarmId);
        activity?.SetTag("alarm.cron", effectiveCron ?? "one-shot");
        activity?.SetTag("alarm.target", targetEntity);

        // Persist the alarm definition
        await _alarmRepository.UpsertAlarmAsync(alarm).ConfigureAwait(false);

        // If the alarm has a NextFireAt, schedule the task
        if (alarm.NextFireAt is not null)
        {
            await ScheduleAlarmTaskAsync(alarm, alarmSoundId);
        }

        _logger.LogInformation(
            "Alarm '{AlarmName}' ({AlarmId}) created — target={Target}, cron={Cron}, nextFire={NextFire}",
            name, alarmId, targetEntity, effectiveCron ?? "one-shot", alarm.NextFireAt);

        var scheduleDescription = effectiveCron is not null
            ? CronScheduleService.Describe(effectiveCron)
            : $"once at {alarm.NextFireAt:HH:mm} on {alarm.NextFireAt:yyyy-MM-dd}";

        return $"Alarm '{name}' (ID: {alarmId}) set — {scheduleDescription} on {FormatTarget(targetEntity)}.";
    }

    /// <summary>
    /// Dismisses a ringing alarm (stops playback) or disables a scheduled alarm.
    /// </summary>
    public async Task<string> DismissAlarmAsync(string alarmId)
    {
        using var activity = ActivitySource.StartActivity("AlarmSkill.DismissAlarm", ActivityKind.Internal);

        var alarm = await ResolveAlarmAsync(alarmId).ConfigureAwait(false);
        if (alarm is null)
            return $"No alarm found matching '{alarmId}'.";

        activity?.SetTag("alarm.id", alarm.Id);

        // Stop any currently ringing task for this alarm
        var ringingTasks = _taskStore.GetByType(ScheduledTaskType.Alarm)
            .OfType<AlarmScheduledTask>()
            .Where(t => t.AlarmClockId == alarm.Id)
            .ToList();

        foreach (var task in ringingTasks)
        {
            _taskStore.TryRemove(task.Id, out _);
            try
            {
                await _taskRepository.UpdateStatusAsync(task.Id, ScheduledTaskStatus.Completed).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update dismissed alarm task {TaskId} status in MongoDB", task.Id);
            }
        }

        // Update the alarm definition
        alarm.LastDismissedAt = _timeProvider.GetUtcNow();

        if (alarm.CronSchedule is not null)
        {
            // Recurring alarm — advance to next occurrence
            _cronService.AdvanceSchedule(alarm);
            await _alarmRepository.UpsertAlarmAsync(alarm).ConfigureAwait(false);

            if (alarm.NextFireAt is not null)
            {
                await ScheduleAlarmTaskAsync(alarm, alarm.AlarmSoundId);
            }

            _logger.LogInformation("Alarm '{AlarmName}' dismissed — next fire at {NextFire}", alarm.Name, alarm.NextFireAt);
            return $"Alarm '{alarm.Name}' dismissed. Next alarm: {alarm.NextFireAt:HH:mm} ({CronScheduleService.Describe(alarm.CronSchedule)}).";
        }
        else
        {
            // One-shot alarm — disable it
            alarm.IsEnabled = false;
            alarm.NextFireAt = null;
            await _alarmRepository.UpsertAlarmAsync(alarm).ConfigureAwait(false);

            _logger.LogInformation("One-shot alarm '{AlarmName}' dismissed and disabled", alarm.Name);
            return $"Alarm '{alarm.Name}' dismissed and disabled.";
        }
    }

    /// <summary>
    /// Snoozes a ringing alarm — stops it now and re-schedules after snooze duration.
    /// </summary>
    public async Task<string> SnoozeAlarmAsync(string alarmId, int snoozeMinutes = 9)
    {
        using var activity = ActivitySource.StartActivity("AlarmSkill.SnoozeAlarm", ActivityKind.Internal);

        if (snoozeMinutes <= 0)
            return "Snooze duration must be greater than zero.";

        var alarm = await ResolveAlarmAsync(alarmId).ConfigureAwait(false);
        if (alarm is null)
            return $"No alarm found matching '{alarmId}'.";

        activity?.SetTag("alarm.id", alarm.Id);
        activity?.SetTag("alarm.snooze_minutes", snoozeMinutes);

        // Stop any currently ringing tasks
        var ringingTasks = _taskStore.GetByType(ScheduledTaskType.Alarm)
            .OfType<AlarmScheduledTask>()
            .Where(t => t.AlarmClockId == alarm.Id)
            .ToList();

        foreach (var task in ringingTasks)
        {
            _taskStore.TryRemove(task.Id, out _);
            try
            {
                await _taskRepository.UpdateStatusAsync(task.Id, ScheduledTaskStatus.Completed).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update snoozed alarm task {TaskId} status in MongoDB", task.Id);
            }
        }

        // Schedule re-fire after snooze
        var snoozeFireAt = _timeProvider.GetUtcNow().AddMinutes(snoozeMinutes);
        alarm.NextFireAt = snoozeFireAt;
        await _alarmRepository.UpsertAlarmAsync(alarm).ConfigureAwait(false);

        await ScheduleAlarmTaskAsync(alarm, alarm.AlarmSoundId);

        _logger.LogInformation(
            "Alarm '{AlarmName}' snoozed for {SnoozeMinutes}m — will fire at {FireAt}",
            alarm.Name, snoozeMinutes, snoozeFireAt);

        return $"Alarm '{alarm.Name}' snoozed for {snoozeMinutes} minutes — will ring again at {snoozeFireAt:HH:mm}.";
    }

    /// <summary>
    /// Lists all alarm clocks with their status and next fire time.
    /// </summary>
    public async Task<string> ListAlarmsAsync()
    {
        var alarms = await _alarmRepository.GetAllAlarmsAsync().ConfigureAwait(false);
        if (alarms.Count == 0)
            return "No alarms configured.";

        var lines = alarms
            .OrderBy(a => a.NextFireAt ?? DateTimeOffset.MaxValue)
            .Select(a =>
            {
                var status = a.IsEnabled ? "enabled" : "disabled";
                var schedule = a.CronSchedule is not null
                    ? CronScheduleService.Describe(a.CronSchedule)
                    : "one-shot";
                var nextFire = a.NextFireAt?.ToString("yyyy-MM-dd HH:mm") ?? "not scheduled";
                return $"- {a.Name} (ID: {a.Id}) [{status}] — {schedule}, next: {nextFire}, target: {FormatTarget(a.TargetEntity)}";
            });

        return $"Alarms:\n{string.Join('\n', lines)}";
    }

    /// <summary>
    /// Gets all configured alarm clocks (for testing/API).
    /// </summary>
    internal async Task<IReadOnlyList<AlarmClock>> GetAllAlarmsInternalAsync()
    {
        return await _alarmRepository.GetAllAlarmsAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Schedules an <see cref="AlarmScheduledTask"/> for the given alarm clock definition.
    /// </summary>
    private async Task ScheduleAlarmTaskAsync(AlarmClock alarm, string? alarmSoundId)
    {
        // Resolve sound URI before creating the task (AlarmSoundUri is init-only)
        string? soundUri = null;
        if (alarmSoundId is not null)
        {
            try
            {
                var sound = await _alarmRepository.GetSoundAsync(alarmSoundId).ConfigureAwait(false);
                soundUri = sound?.MediaSourceUri;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to resolve alarm sound {SoundId} — will use TTS fallback", alarmSoundId);
            }
        }
        else
        {
            // Try to use default sound
            try
            {
                var defaultSound = await _alarmRepository.GetDefaultSoundAsync().ConfigureAwait(false);
                soundUri = defaultSound?.MediaSourceUri;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to resolve default alarm sound — will use TTS fallback");
            }
        }

        var taskId = Guid.NewGuid().ToString("N");
        var task = new AlarmScheduledTask
        {
            Id = alarm.Id,
            TaskId = taskId,
            Label = alarm.Name,
            FireAt = alarm.NextFireAt!.Value,
            AlarmClockId = alarm.Id,
            TargetEntity = alarm.TargetEntity,
            AlarmSoundUri = soundUri,
            PlaybackInterval = alarm.PlaybackInterval,
            AutoDismissAfter = alarm.AutoDismissAfter
        };

        // Remove any existing task for this alarm before scheduling new one
        _taskStore.TryRemove(alarm.Id, out _);
        _taskStore.Add(task);
    }

    /// <summary>
    /// Resolves an alarm by ID or by name (case-insensitive).
    /// </summary>
    private async Task<AlarmClock?> ResolveAlarmAsync(string alarmIdOrName)
    {
        // Try direct ID lookup first
        var alarm = await _alarmRepository.GetAlarmAsync(alarmIdOrName).ConfigureAwait(false);
        if (alarm is not null) return alarm;

        // Fall back to name search
        var alarms = await _alarmRepository.GetAllAlarmsAsync().ConfigureAwait(false);
        return alarms.FirstOrDefault(a =>
            a.Name.Equals(alarmIdOrName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Resolves a location name to a media_player entity_id.
    /// The special value "presence" is passed through as-is for runtime resolution.
    /// </summary>
    private async Task<string?> ResolveMediaPlayerEntityAsync(string input)
    {
        if (input.Equals("presence", StringComparison.OrdinalIgnoreCase))
            return "presence";

        if (input.Contains('.'))
            return input;

        var entities = await _entityLocationService.FindEntitiesByLocationAsync(
            input,
            domainFilter: ["media_player"],
            ct: default).ConfigureAwait(false);

        if (entities.Count > 0)
        {
            _logger.LogDebug(
                "Found {Count} media_player(s) in '{Location}': {Entities}",
                entities.Count, input,
                string.Join(", ", entities.Select(e => e.EntityId)));
            return entities[0].EntityId;
        }

        _logger.LogWarning("No media_player entity found for location '{Location}'", input);
        return null;
    }

    /// <summary>
    /// Computes the next fire time for a one-shot alarm at the given hour/minute.
    /// If the time has already passed today, schedules for tomorrow.
    /// </summary>
    private DateTimeOffset ComputeNextFireAt(int hour, int minute)
    {
        var now = _timeProvider.GetUtcNow();
        var today = new DateTimeOffset(now.Year, now.Month, now.Day, hour, minute, 0, now.Offset);
        return today > now ? today : today.AddDays(1);
    }

    private static bool TryParseTime(string time, out int hour, out int minute)
    {
        hour = 0;
        minute = 0;

        if (string.IsNullOrWhiteSpace(time)) return false;

        var parts = time.Split(':');
        if (parts.Length != 2) return false;

        return int.TryParse(parts[0], out hour)
            && int.TryParse(parts[1], out minute)
            && hour is >= 0 and <= 23
            && minute is >= 0 and <= 59;
    }

    private static string FormatTarget(string targetEntity) =>
        targetEntity == "presence" ? "presence-based (dynamic)" : targetEntity;
}
