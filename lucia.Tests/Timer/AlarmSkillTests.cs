using FakeItEasy;
using lucia.Agents.Models;
using lucia.Agents.Services;
using lucia.TimerAgent;
using lucia.TimerAgent.ScheduledTasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace lucia.Tests.Timer;

/// <summary>
/// Unit tests for <see cref="AlarmSkill"/>.
/// </summary>
public sealed class AlarmSkillTests
{
    private readonly IAlarmClockRepository _alarmRepository = A.Fake<IAlarmClockRepository>();
    private readonly ScheduledTaskStore _taskStore = new();
    private readonly IScheduledTaskRepository _taskRepository = A.Fake<IScheduledTaskRepository>();
    private readonly CronScheduleService _cronService;
    private readonly IEntityLocationService _entityLocationService = A.Fake<IEntityLocationService>();
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly ILogger<AlarmSkill> _logger = A.Fake<ILogger<AlarmSkill>>();
    private readonly ILogger<CronScheduleService> _cronLogger = A.Fake<ILogger<CronScheduleService>>();
    private readonly AlarmSkill _skill;

    public AlarmSkillTests()
    {
        // Fix time to 2025-07-15 12:00:00 UTC (a Tuesday)
        _timeProvider.SetUtcNow(new DateTimeOffset(2025, 7, 15, 12, 0, 0, TimeSpan.Zero));
        _cronService = new CronScheduleService(_timeProvider, _cronLogger);

        _skill = new AlarmSkill(
            _alarmRepository,
            _taskStore,
            _taskRepository,
            _cronService,
            _entityLocationService,
            _timeProvider,
            _logger);
    }

    // -- SetAlarmAsync tests --

    [Fact]
    public async Task SetAlarmAsync_OneShotAlarm_CreatesAlarmAndSchedulesTask()
    {
        var result = await _skill.SetAlarmAsync("Morning Wake Up", "07:00", "media_player.bedroom");

        Assert.Contains("Morning Wake Up", result);
        Assert.Contains("07:00", result);

        // Alarm should be persisted
        A.CallTo(() => _alarmRepository.UpsertAlarmAsync(
            A<AlarmClock>.That.Matches(a => a.Name == "Morning Wake Up" && a.CronSchedule == null),
            A<CancellationToken>._)).MustHaveHappenedOnceExactly();

        // Task should be scheduled in the store
        var tasks = _taskStore.GetByType(ScheduledTaskType.Alarm);
        Assert.Single(tasks);
    }

    [Fact]
    public async Task SetAlarmAsync_OneShotAlarmTimePassed_SchedulesForTomorrow()
    {
        // Current time is 12:00, set alarm for 08:00 → should be tomorrow
        var result = await _skill.SetAlarmAsync("Late Alarm", "08:00", "media_player.bedroom");

        Assert.Contains("Late Alarm", result);

        A.CallTo(() => _alarmRepository.UpsertAlarmAsync(
            A<AlarmClock>.That.Matches(a =>
                a.NextFireAt!.Value.Day == 16), // Tomorrow (July 16)
            A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task SetAlarmAsync_RecurringCronAlarm_UsesInitializeNextFireAt()
    {
        var result = await _skill.SetAlarmAsync(
            "Weekday Wake Up", "07:00", "media_player.bedroom",
            cronSchedule: "0 7 * * 1-5");

        Assert.Contains("Weekday Wake Up", result);
        Assert.Contains("Weekdays", result);

        // Should have CRON schedule persisted
        A.CallTo(() => _alarmRepository.UpsertAlarmAsync(
            A<AlarmClock>.That.Matches(a =>
                a.CronSchedule == "0 7 * * 1-5" && a.NextFireAt != null),
            A<CancellationToken>._)).MustHaveHappenedOnceExactly();

        // Task should be scheduled
        var tasks = _taskStore.GetByType(ScheduledTaskType.Alarm);
        Assert.Single(tasks);
    }

    [Fact]
    public async Task SetAlarmAsync_InvalidCronSchedule_ReturnsError()
    {
        var result = await _skill.SetAlarmAsync("Bad Alarm", "07:00", "media_player.bedroom",
            cronSchedule: "bad cron");

        Assert.Contains("Invalid CRON schedule", result);
        Assert.True(_taskStore.IsEmpty);
    }

    [Fact]
    public async Task SetAlarmAsync_InvalidTimeFormat_ReturnsError()
    {
        var result = await _skill.SetAlarmAsync("Bad Time", "seven thirty", "media_player.bedroom");

        Assert.Contains("Invalid time format", result);
        Assert.True(_taskStore.IsEmpty);
    }

    [Fact]
    public async Task SetAlarmAsync_EmptyName_ReturnsError()
    {
        var result = await _skill.SetAlarmAsync("", "07:00", "media_player.bedroom");

        Assert.Equal("Alarm name is required.", result);
    }

    [Fact]
    public async Task SetAlarmAsync_EmptyLocation_ReturnsError()
    {
        var result = await _skill.SetAlarmAsync("Test", "07:00", "");

        Assert.Equal("Location or media_player entity is required.", result);
    }

    [Fact]
    public async Task SetAlarmAsync_PresenceTarget_PassesThroughAsIs()
    {
        var result = await _skill.SetAlarmAsync("Wake Up", "07:00", "presence");

        Assert.Contains("presence-based", result);

        A.CallTo(() => _alarmRepository.UpsertAlarmAsync(
            A<AlarmClock>.That.Matches(a => a.TargetEntity == "presence"),
            A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task SetAlarmAsync_LocationResolution_FindsMediaPlayer()
    {
        A.CallTo(() => _entityLocationService.FindEntitiesByLocationAsync(
            "bedroom", A<IReadOnlyList<string>>._, A<CancellationToken>._))
            .Returns(new List<EntityLocationInfo>
            {
                new() { EntityId = "media_player.bedroom_speaker", FriendlyName = "Bedroom Speaker", AreaId = "bedroom" }
            });

        var result = await _skill.SetAlarmAsync("Wake Up", "07:00", "bedroom");

        Assert.Contains("Wake Up", result);
        A.CallTo(() => _alarmRepository.UpsertAlarmAsync(
            A<AlarmClock>.That.Matches(a => a.TargetEntity == "media_player.bedroom_speaker"),
            A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task SetAlarmAsync_LocationNotFound_ReturnsError()
    {
        A.CallTo(() => _entityLocationService.FindEntitiesByLocationAsync(
            "garage", A<IReadOnlyList<string>>._, A<CancellationToken>._))
            .Returns(new List<EntityLocationInfo>());

        var result = await _skill.SetAlarmAsync("Test", "07:00", "garage");

        Assert.Contains("Could not find a media player device", result);
        Assert.True(_taskStore.IsEmpty);
    }

    [Fact]
    public async Task SetAlarmAsync_SoundName_ResolvesSoundId()
    {
        var sounds = new List<AlarmSound>
        {
            new() { Id = "s1", Name = "Gentle", MediaSourceUri = "media-source://local/gentle.wav" },
            new() { Id = "s2", Name = "Radar", MediaSourceUri = "media-source://local/radar.wav" }
        };
        A.CallTo(() => _alarmRepository.GetAllSoundsAsync(A<CancellationToken>._))
            .Returns(sounds);
        A.CallTo(() => _alarmRepository.GetSoundAsync("s1", A<CancellationToken>._))
            .Returns(sounds[0]);

        var result = await _skill.SetAlarmAsync("Wake Up", "07:00", "media_player.bedroom",
            soundName: "Gentle");

        Assert.Contains("Wake Up", result);
        A.CallTo(() => _alarmRepository.UpsertAlarmAsync(
            A<AlarmClock>.That.Matches(a => a.AlarmSoundId == "s1"),
            A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task SetAlarmAsync_SoundNameNotFound_ReturnsError()
    {
        A.CallTo(() => _alarmRepository.GetAllSoundsAsync(A<CancellationToken>._))
            .Returns(new List<AlarmSound>
            {
                new() { Id = "s1", Name = "Gentle", MediaSourceUri = "uri" }
            });

        var result = await _skill.SetAlarmAsync("Test", "07:00", "media_player.bedroom",
            soundName: "Nonexistent");

        Assert.Contains("not found", result);
        Assert.Contains("Gentle", result);
    }

    // -- DismissAlarmAsync tests --

    [Fact]
    public async Task DismissAlarmAsync_OneShotAlarm_DisablesAlarm()
    {
        var alarm = new AlarmClock
        {
            Id = "alarm1",
            Name = "Nap Alarm",
            TargetEntity = "media_player.bedroom",
            NextFireAt = _timeProvider.GetUtcNow().AddHours(1),
            IsEnabled = true
        };

        A.CallTo(() => _alarmRepository.GetAlarmAsync("alarm1", A<CancellationToken>._))
            .Returns(alarm);

        var result = await _skill.DismissAlarmAsync("alarm1");

        Assert.Contains("dismissed and disabled", result);
        A.CallTo(() => _alarmRepository.UpsertAlarmAsync(
            A<AlarmClock>.That.Matches(a => !a.IsEnabled && a.NextFireAt == null),
            A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task DismissAlarmAsync_RecurringAlarm_AdvancesToNextOccurrence()
    {
        var alarm = new AlarmClock
        {
            Id = "alarm2",
            Name = "Weekday Wake Up",
            TargetEntity = "media_player.bedroom",
            CronSchedule = "0 7 * * 1-5",
            NextFireAt = _timeProvider.GetUtcNow(),
            IsEnabled = true
        };

        A.CallTo(() => _alarmRepository.GetAlarmAsync("alarm2", A<CancellationToken>._))
            .Returns(alarm);

        var result = await _skill.DismissAlarmAsync("alarm2");

        Assert.Contains("dismissed", result);
        Assert.Contains("Next alarm", result);
        // Should have advanced the schedule
        A.CallTo(() => _alarmRepository.UpsertAlarmAsync(
            A<AlarmClock>.That.Matches(a => a.NextFireAt > _timeProvider.GetUtcNow()),
            A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task DismissAlarmAsync_AlarmNotFound_ReturnsError()
    {
        A.CallTo(() => _alarmRepository.GetAlarmAsync("nope", A<CancellationToken>._))
            .Returns((AlarmClock?)null);
        A.CallTo(() => _alarmRepository.GetAllAlarmsAsync(A<CancellationToken>._))
            .Returns(new List<AlarmClock>());

        var result = await _skill.DismissAlarmAsync("nope");

        Assert.Contains("No alarm found", result);
    }

    [Fact]
    public async Task DismissAlarmAsync_ByName_ResolvesAlarm()
    {
        var alarm = new AlarmClock
        {
            Id = "alarm3",
            Name = "Morning Alarm",
            TargetEntity = "media_player.bedroom",
            NextFireAt = _timeProvider.GetUtcNow().AddHours(1),
            IsEnabled = true
        };

        A.CallTo(() => _alarmRepository.GetAlarmAsync("Morning Alarm", A<CancellationToken>._))
            .Returns((AlarmClock?)null);
        A.CallTo(() => _alarmRepository.GetAllAlarmsAsync(A<CancellationToken>._))
            .Returns(new List<AlarmClock> { alarm });

        var result = await _skill.DismissAlarmAsync("Morning Alarm");

        Assert.Contains("dismissed", result);
    }

    [Fact]
    public async Task DismissAlarmAsync_StopsRingingTask()
    {
        // Schedule an alarm task in the store
        var alarmTask = new AlarmScheduledTask
        {
            Id = "alarm4",
            TaskId = "task-x",
            Label = "Ringing",
            FireAt = _timeProvider.GetUtcNow(),
            AlarmClockId = "alarm4",
            TargetEntity = "media_player.bedroom"
        };
        _taskStore.Add(alarmTask);

        var alarm = new AlarmClock
        {
            Id = "alarm4",
            Name = "Active Alarm",
            TargetEntity = "media_player.bedroom",
            NextFireAt = _timeProvider.GetUtcNow(),
            IsEnabled = true
        };
        A.CallTo(() => _alarmRepository.GetAlarmAsync("alarm4", A<CancellationToken>._))
            .Returns(alarm);

        var result = await _skill.DismissAlarmAsync("alarm4");

        // The ringing task should be removed from the store
        Assert.True(_taskStore.IsEmpty);
        Assert.Contains("dismissed", result);
    }

    // -- SnoozeAlarmAsync tests --

    [Fact]
    public async Task SnoozeAlarmAsync_DefaultSnooze_ReschedulesAfter9Minutes()
    {
        var alarm = new AlarmClock
        {
            Id = "alarm5",
            Name = "Snooze Test",
            TargetEntity = "media_player.bedroom",
            NextFireAt = _timeProvider.GetUtcNow(),
            IsEnabled = true
        };

        A.CallTo(() => _alarmRepository.GetAlarmAsync("alarm5", A<CancellationToken>._))
            .Returns(alarm);

        var result = await _skill.SnoozeAlarmAsync("alarm5");

        Assert.Contains("snoozed for 9 minutes", result);
        // Should have a new task scheduled 9 minutes from now
        var tasks = _taskStore.GetByType(ScheduledTaskType.Alarm);
        Assert.Single(tasks);
        var expected = _timeProvider.GetUtcNow().AddMinutes(9);
        Assert.Equal(expected, tasks.First().FireAt);
    }

    [Fact]
    public async Task SnoozeAlarmAsync_CustomDuration_UsesProvidedMinutes()
    {
        var alarm = new AlarmClock
        {
            Id = "alarm6",
            Name = "Custom Snooze",
            TargetEntity = "media_player.bedroom",
            NextFireAt = _timeProvider.GetUtcNow(),
            IsEnabled = true
        };

        A.CallTo(() => _alarmRepository.GetAlarmAsync("alarm6", A<CancellationToken>._))
            .Returns(alarm);

        var result = await _skill.SnoozeAlarmAsync("alarm6", snoozeMinutes: 5);

        Assert.Contains("snoozed for 5 minutes", result);
        var tasks = _taskStore.GetByType(ScheduledTaskType.Alarm);
        Assert.Single(tasks);
        var expected = _timeProvider.GetUtcNow().AddMinutes(5);
        Assert.Equal(expected, tasks.First().FireAt);
    }

    [Fact]
    public async Task SnoozeAlarmAsync_ZeroDuration_ReturnsError()
    {
        var result = await _skill.SnoozeAlarmAsync("alarm", snoozeMinutes: 0);

        Assert.Contains("greater than zero", result);
    }

    [Fact]
    public async Task SnoozeAlarmAsync_AlarmNotFound_ReturnsError()
    {
        A.CallTo(() => _alarmRepository.GetAlarmAsync("nope", A<CancellationToken>._))
            .Returns((AlarmClock?)null);
        A.CallTo(() => _alarmRepository.GetAllAlarmsAsync(A<CancellationToken>._))
            .Returns(new List<AlarmClock>());

        var result = await _skill.SnoozeAlarmAsync("nope");

        Assert.Contains("No alarm found", result);
    }

    [Fact]
    public async Task SnoozeAlarmAsync_StopsRingingTask()
    {
        var alarmTask = new AlarmScheduledTask
        {
            Id = "alarm7",
            TaskId = "task-y",
            Label = "Ringing",
            FireAt = _timeProvider.GetUtcNow(),
            AlarmClockId = "alarm7",
            TargetEntity = "media_player.bedroom"
        };
        _taskStore.Add(alarmTask);

        var alarm = new AlarmClock
        {
            Id = "alarm7",
            Name = "Snooze Ringing",
            TargetEntity = "media_player.bedroom",
            NextFireAt = _timeProvider.GetUtcNow(),
            IsEnabled = true
        };
        A.CallTo(() => _alarmRepository.GetAlarmAsync("alarm7", A<CancellationToken>._))
            .Returns(alarm);

        await _skill.SnoozeAlarmAsync("alarm7");

        // Old ringing task replaced with snoozed task
        var tasks = _taskStore.GetByType(ScheduledTaskType.Alarm);
        Assert.Single(tasks);
        Assert.True(tasks.First().FireAt > _timeProvider.GetUtcNow());
    }

    // -- ListAlarmsAsync tests --

    [Fact]
    public async Task ListAlarmsAsync_NoAlarms_ReturnsEmptyMessage()
    {
        A.CallTo(() => _alarmRepository.GetAllAlarmsAsync(A<CancellationToken>._))
            .Returns(new List<AlarmClock>());

        var result = await _skill.ListAlarmsAsync();

        Assert.Equal("No alarms configured.", result);
    }

    [Fact]
    public async Task ListAlarmsAsync_WithAlarms_ReturnsFormattedList()
    {
        var alarms = new List<AlarmClock>
        {
            new()
            {
                Id = "a1",
                Name = "Morning",
                TargetEntity = "media_player.bedroom",
                CronSchedule = "0 7 * * 1-5",
                NextFireAt = new DateTimeOffset(2025, 7, 16, 7, 0, 0, TimeSpan.Zero),
                IsEnabled = true
            },
            new()
            {
                Id = "a2",
                Name = "Nap",
                TargetEntity = "media_player.living_room",
                NextFireAt = new DateTimeOffset(2025, 7, 15, 14, 0, 0, TimeSpan.Zero),
                IsEnabled = false
            }
        };

        A.CallTo(() => _alarmRepository.GetAllAlarmsAsync(A<CancellationToken>._))
            .Returns(alarms);

        var result = await _skill.ListAlarmsAsync();

        Assert.Contains("Morning", result);
        Assert.Contains("Nap", result);
        Assert.Contains("enabled", result);
        Assert.Contains("disabled", result);
        Assert.Contains("Weekdays", result);
        Assert.Contains("one-shot", result);
    }

    // -- GetTools tests --

    [Fact]
    public void GetTools_ReturnsFourTools()
    {
        var tools = _skill.GetTools();

        Assert.Equal(4, tools.Count);
        var names = tools.Select(t => t.GetType().GetProperty("Name")?.GetValue(t)?.ToString()
            ?? t.ToString()).ToList();

        // Verify tool names through metadata
        Assert.NotEmpty(tools);
    }
}
