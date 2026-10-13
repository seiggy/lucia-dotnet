using lucia.TimerAgent.ScheduledTasks;

namespace lucia.Tests.Data.Providers;

/// <summary>
/// Behaviour every <see cref="IAlarmClockRepository"/> provider must share.
/// </summary>
public abstract class AlarmClockRepositoryContract
{
    protected abstract Task<StoreScope<IAlarmClockRepository>> CreateAsync();

    [Fact]
    public async Task GetAllAlarmsAsync_EmptyStore_ReturnsEmpty()
    {
        await using var scope = await CreateAsync();

        Assert.Empty(await scope.Store.GetAllAlarmsAsync());
    }

    [Fact]
    public async Task UpsertAlarmAsync_NewAlarm_RoundTripsAllFields()
    {
        await using var scope = await CreateAsync();

        var alarm = new AlarmClock
        {
            Id = "alarm-1",
            Name = "Morning Wake Up",
            TargetEntity = "media_player.bedroom",
            AlarmSoundId = "sound-1",
            CronSchedule = "0 7 * * 1-5",
            NextFireAt = DateTimeOffset.UtcNow.AddDays(1),
            PlaybackInterval = TimeSpan.FromSeconds(45),
            AutoDismissAfter = TimeSpan.FromMinutes(15),
            LastDismissedAt = DateTimeOffset.UtcNow.AddDays(-1),
            IsEnabled = true,
            VolumeStart = 0.1,
            VolumeEnd = 0.9,
            VolumeRampDuration = TimeSpan.FromSeconds(25),
        };

        await scope.Store.UpsertAlarmAsync(alarm);

        var stored = await scope.Store.GetAlarmAsync(alarm.Id);
        Assert.NotNull(stored);
        Assert.Equal(alarm.Name, stored!.Name);
        Assert.Equal(alarm.TargetEntity, stored.TargetEntity);
        Assert.Equal(alarm.AlarmSoundId, stored.AlarmSoundId);
        Assert.Equal(alarm.CronSchedule, stored.CronSchedule);
        Assert.Equal(alarm.NextFireAt, stored.NextFireAt);
        Assert.Equal(alarm.PlaybackInterval, stored.PlaybackInterval);
        Assert.Equal(alarm.AutoDismissAfter, stored.AutoDismissAfter);
        Assert.Equal(alarm.LastDismissedAt, stored.LastDismissedAt);
        Assert.Equal(alarm.IsEnabled, stored.IsEnabled);
        Assert.Equal(alarm.VolumeStart, stored.VolumeStart);
        Assert.Equal(alarm.VolumeEnd, stored.VolumeEnd);
        Assert.Equal(alarm.VolumeRampDuration, stored.VolumeRampDuration);
    }

    [Fact]
    public async Task GetDueAlarmsAsync_OnlyReturnsEnabledDueAlarms()
    {
        await using var scope = await CreateAsync();
        var now = DateTimeOffset.UtcNow;

        await scope.Store.UpsertAlarmAsync(new AlarmClock { Id = "due-enabled", Name = "Due", TargetEntity = "media_player.office", IsEnabled = true, NextFireAt = now.AddMinutes(-5) });
        await scope.Store.UpsertAlarmAsync(new AlarmClock { Id = "due-disabled", Name = "Disabled", TargetEntity = "media_player.office", IsEnabled = false, NextFireAt = now.AddMinutes(-5) });
        await scope.Store.UpsertAlarmAsync(new AlarmClock { Id = "future-enabled", Name = "Future", TargetEntity = "media_player.office", IsEnabled = true, NextFireAt = now.AddMinutes(5) });

        var due = await scope.Store.GetDueAlarmsAsync(now);

        Assert.Contains(due, alarm => alarm.Id == "due-enabled");
        Assert.DoesNotContain(due, alarm => alarm.Id == "due-disabled");
        Assert.DoesNotContain(due, alarm => alarm.Id == "future-enabled");
    }

    [Fact]
    public async Task UpsertAlarmAsync_ExistingAlarm_ReplacesInsteadOfDuplicating()
    {
        await using var scope = await CreateAsync();

        await scope.Store.UpsertAlarmAsync(new AlarmClock { Id = "alarm-2", Name = "Before", TargetEntity = "media_player.kitchen", IsEnabled = true });
        await scope.Store.UpsertAlarmAsync(new AlarmClock { Id = "alarm-2", Name = "After", TargetEntity = "media_player.living_room", IsEnabled = false });

        var alarms = await scope.Store.GetAllAlarmsAsync();
        var stored = Assert.Single(alarms);
        Assert.Equal("After", stored.Name);
        Assert.Equal("media_player.living_room", stored.TargetEntity);
        Assert.False(stored.IsEnabled);
    }

    [Fact]
    public async Task DeleteAlarmAsync_RemovesOnlyTheTargetAlarm()
    {
        await using var scope = await CreateAsync();

        await scope.Store.UpsertAlarmAsync(new AlarmClock { Id = "alarm-a", Name = "A", TargetEntity = "media_player.a", IsEnabled = true });
        await scope.Store.UpsertAlarmAsync(new AlarmClock { Id = "alarm-b", Name = "B", TargetEntity = "media_player.b", IsEnabled = true });

        await scope.Store.DeleteAlarmAsync("alarm-a");

        var alarms = await scope.Store.GetAllAlarmsAsync();
        var remaining = Assert.Single(alarms);
        Assert.Equal("alarm-b", remaining.Id);
    }

    [Fact]
    public async Task GetAllSoundsAsync_AndDefaultSound_RoundTripDefaultAndDeleteSemantics()
    {
        await using var scope = await CreateAsync();

        await scope.Store.UpsertSoundAsync(new AlarmSound { Id = "sound-1", Name = "Gentle", MediaSourceUri = "media-source://media_source/local/alarms/gentle.wav", IsDefault = true });
        await scope.Store.UpsertSoundAsync(new AlarmSound { Id = "sound-2", Name = "Radar", MediaSourceUri = "media-source://media_source/local/alarms/radar.wav", IsDefault = false });

        var sounds = await scope.Store.GetAllSoundsAsync();
        Assert.Equal(2, sounds.Count);

        var defaultSound = await scope.Store.GetDefaultSoundAsync();
        Assert.NotNull(defaultSound);
        Assert.Equal("sound-1", defaultSound!.Id);

        await scope.Store.DeleteSoundAsync("sound-2");

        var remaining = await scope.Store.GetAllSoundsAsync();
        Assert.Single(remaining);
        Assert.Equal("sound-1", remaining[0].Id);
    }

    [Fact]
    public async Task UpsertSoundAsync_ExistingSound_ReplacesInsteadOfDuplicating()
    {
        await using var scope = await CreateAsync();

        await scope.Store.UpsertSoundAsync(new AlarmSound { Id = "sound-3", Name = "Old", MediaSourceUri = "media-source://media_source/local/alarms/old.wav", IsDefault = false });
        await scope.Store.UpsertSoundAsync(new AlarmSound { Id = "sound-3", Name = "New", MediaSourceUri = "media-source://media_source/local/alarms/new.wav", IsDefault = true });

        var sound = await scope.Store.GetSoundAsync("sound-3");
        Assert.NotNull(sound);
        Assert.Equal("New", sound!.Name);
        Assert.True(sound.IsDefault);
    }
}
