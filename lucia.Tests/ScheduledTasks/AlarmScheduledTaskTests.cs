using FakeItEasy;
using lucia.HomeAssistant.Models;
using lucia.HomeAssistant.Services;
using lucia.TimerAgent.ScheduledTasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace lucia.Tests.ScheduledTasks;

public sealed class AlarmScheduledTaskTests
{
    [Fact]
    public void TaskType_IsAlarm()
    {
        var task = CreateAlarmTask();
        Assert.Equal(ScheduledTaskType.Alarm, task.TaskType);
    }

    [Fact]
    public void IsExpired_ReturnsTrue_WhenPastFireAt()
    {
        var task = CreateAlarmTask(fireAt: DateTimeOffset.UtcNow.AddMinutes(-1));
        Assert.True(task.IsExpired(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void IsExpired_ReturnsFalse_WhenBeforeFireAt()
    {
        var task = CreateAlarmTask(fireAt: DateTimeOffset.UtcNow.AddMinutes(5));
        Assert.False(task.IsExpired(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void ToDocument_PreservesAllFields()
    {
        var task = CreateAlarmTask(
            alarmSoundUri: "media-source://media_source/local/alarms/gentle.wav",
            playbackInterval: TimeSpan.FromSeconds(45),
            autoDismissAfter: TimeSpan.FromMinutes(5));

        var doc = task.ToDocument();

        Assert.Equal("alarm-1", doc.Id);
        Assert.Equal("task-alarm-1", doc.TaskId);
        Assert.Equal("Morning Wake Up", doc.Label);
        Assert.Equal(ScheduledTaskType.Alarm, doc.TaskType);
        Assert.Equal(ScheduledTaskStatus.Pending, doc.Status);
        Assert.Equal("clock-1", doc.AlarmClockId);
        Assert.Equal("media_player.bedroom", doc.TargetEntity);
        Assert.Equal("media-source://media_source/local/alarms/gentle.wav", doc.AlarmSoundUri);
        Assert.Equal(TimeSpan.FromSeconds(45), doc.PlaybackInterval);
        Assert.Equal(TimeSpan.FromMinutes(5), doc.AutoDismissAfter);
    }

    [Fact]
    public async Task ExecuteAsync_PlaysMediaWithAnnounce_WhenSoundUriProvided()
    {
        var task = CreateAlarmTask(
            alarmSoundUri: "media-source://media_source/local/alarms/alarm.wav",
            playbackInterval: TimeSpan.FromSeconds(1),
            autoDismissAfter: TimeSpan.FromMilliseconds(500));

        var haClient = A.Fake<IHomeAssistantClient>();
        var sp = BuildServiceProvider(haClient);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await task.ExecuteAsync(sp, cts.Token);

        // Should have called media_player.play_media at least once
        A.CallTo(() => haClient.CallServiceAsync(
            "media_player",
            "play_media",
            A<string?>._,
            A<ServiceCallRequest?>.That.Matches(r =>
                r != null
                && r.EntityId == "media_player.bedroom"
                && r.ContainsKey("announce")
                && r.ContainsKey("media_content_id")),
            A<CancellationToken>._)).MustHaveHappened();
    }

    [Fact]
    public async Task ExecuteAsync_FallsBackToTtsAnnounce_WhenNoSoundUri()
    {
        var task = CreateAlarmTask(
            alarmSoundUri: null,
            playbackInterval: TimeSpan.FromSeconds(1),
            autoDismissAfter: TimeSpan.FromMilliseconds(500));

        var haClient = A.Fake<IHomeAssistantClient>();
        var sp = BuildServiceProvider(haClient);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await task.ExecuteAsync(sp, cts.Token);

        // Should have called assist_satellite.announce
        A.CallTo(() => haClient.CallServiceAsync(
            "assist_satellite",
            "announce",
            A<string?>._,
            A<ServiceCallRequest?>.That.Matches(r =>
                r != null
                && r.EntityId == "media_player.bedroom"
                && r.ContainsKey("message")),
            A<CancellationToken>._)).MustHaveHappened();
    }

    [Fact]
    public async Task ExecuteAsync_AutoDismisses_AfterTimeout()
    {
        var task = CreateAlarmTask(
            alarmSoundUri: "media-source://media_source/local/alarms/alarm.wav",
            playbackInterval: TimeSpan.FromSeconds(10),
            autoDismissAfter: TimeSpan.FromMilliseconds(200));

        var haClient = A.Fake<IHomeAssistantClient>();
        var sp = BuildServiceProvider(haClient);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Should complete within ~200ms (auto-dismiss), not wait full 5s
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await task.ExecuteAsync(sp, cts.Token);
        sw.Stop();

        // Allow generous margin but should be much less than 5s
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3),
            $"Expected auto-dismiss within ~200ms, took {sw.Elapsed}");
    }

    // -- Helpers --

    private static AlarmScheduledTask CreateAlarmTask(
        string id = "alarm-1",
        DateTimeOffset? fireAt = null,
        string? alarmSoundUri = "media-source://media_source/local/alarms/alarm.wav",
        TimeSpan? playbackInterval = null,
        TimeSpan? autoDismissAfter = null)
    {
        return new AlarmScheduledTask
        {
            Id = id,
            TaskId = $"task-{id}",
            Label = "Morning Wake Up",
            FireAt = fireAt ?? DateTimeOffset.UtcNow.AddMinutes(-1),
            AlarmClockId = "clock-1",
            TargetEntity = "media_player.bedroom",
            AlarmSoundUri = alarmSoundUri,
            PlaybackInterval = playbackInterval ?? TimeSpan.FromSeconds(30),
            AutoDismissAfter = autoDismissAfter ?? TimeSpan.FromMinutes(10)
        };
    }

    private static IServiceProvider BuildServiceProvider(IHomeAssistantClient haClient)
    {
        var sc = new ServiceCollection();
        sc.AddSingleton(haClient);
        sc.AddSingleton(A.Fake<ILogger<AlarmScheduledTask>>());
        return sc.BuildServiceProvider();
    }
}
