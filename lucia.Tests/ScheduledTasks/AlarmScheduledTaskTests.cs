using FakeItEasy;
using lucia.Agents.Models;
using lucia.Agents.Services;
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

    [Fact]
    public async Task ExecuteAsync_PlaysMultipleTimes_AtPlaybackInterval()
    {
        var task = CreateAlarmTask(
            alarmSoundUri: "media-source://media_source/local/alarms/alarm.wav",
            playbackInterval: TimeSpan.FromMilliseconds(30),
            autoDismissAfter: TimeSpan.FromMilliseconds(200));

        var haClient = A.Fake<IHomeAssistantClient>();
        var sp = BuildServiceProvider(haClient);

        await task.ExecuteAsync(sp, CancellationToken.None);

        // With 30ms interval and 200ms timeout, expect at least 2 plays
        A.CallTo(() => haClient.CallServiceAsync(
            "media_player",
            "play_media",
            A<string?>._,
            A<ServiceCallRequest?>._,
            A<CancellationToken>._)).MustHaveHappenedANumberOfTimesMatching(n => n >= 2);
    }

    [Fact]
    public async Task ExecuteAsync_ContinuesLoop_WhenPlaybackFails()
    {
        var callCount = 0;
        var haClient = A.Fake<IHomeAssistantClient>();
        A.CallTo(() => haClient.CallServiceAsync(
            "media_player",
            "play_media",
            A<string?>._,
            A<ServiceCallRequest?>._,
            A<CancellationToken>._))
            .Invokes(() =>
            {
                callCount++;
                if (callCount == 1) throw new HttpRequestException("Network error");
            });

        var task = CreateAlarmTask(
            alarmSoundUri: "media-source://media_source/local/alarms/alarm.wav",
            playbackInterval: TimeSpan.FromMilliseconds(20),
            autoDismissAfter: TimeSpan.FromMilliseconds(200));

        var sp = BuildServiceProvider(haClient);
        await task.ExecuteAsync(sp, CancellationToken.None);

        // Should have retried after the first failure
        Assert.True(callCount >= 2, $"Expected at least 2 play attempts, got {callCount}");
    }

    [Fact]
    public async Task ExecuteAsync_ExternalCancellation_CompletesQuickly()
    {
        // When externally cancelled, the alarm should stop promptly
        var task = CreateAlarmTask(
            alarmSoundUri: "media-source://media_source/local/alarms/alarm.wav",
            playbackInterval: TimeSpan.FromSeconds(30),
            autoDismissAfter: TimeSpan.FromMinutes(10));

        var haClient = A.Fake<IHomeAssistantClient>();
        var sp = BuildServiceProvider(haClient);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await task.ExecuteAsync(sp, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // External cancellation may propagate — that's acceptable
        }
        sw.Stop();

        // Should complete quickly due to cancellation, not wait for 10min auto-dismiss
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3),
            $"Expected quick completion on external cancel, took {sw.Elapsed}");
    }

    [Fact]
    public void ToDocument_NullSoundUri_PreservesNull()
    {
        var task = CreateAlarmTask(alarmSoundUri: null);
        var doc = task.ToDocument();

        Assert.Null(doc.AlarmSoundUri);
    }

    // -- AlarmClock model tests --

    [Fact]
    public void AlarmClock_Defaults_AreCorrect()
    {
        var alarm = new AlarmClock
        {
            Id = "a1",
            Name = "Test",
            TargetEntity = "media_player.bedroom"
        };

        Assert.Equal(TimeSpan.FromSeconds(30), alarm.PlaybackInterval);
        Assert.Equal(TimeSpan.FromMinutes(10), alarm.AutoDismissAfter);
        Assert.True(alarm.IsEnabled);
        Assert.Null(alarm.CronSchedule);
        Assert.Null(alarm.NextFireAt);
        Assert.Null(alarm.AlarmSoundId);
        Assert.Null(alarm.LastDismissedAt);
    }

    [Fact]
    public void AlarmClock_OneShotSchedule_HasNullCron()
    {
        var alarm = new AlarmClock
        {
            Id = "a2",
            Name = "One-shot",
            TargetEntity = "media_player.bedroom",
            NextFireAt = DateTimeOffset.UtcNow.AddHours(8)
        };

        Assert.Null(alarm.CronSchedule);
        Assert.NotNull(alarm.NextFireAt);
    }

    [Fact]
    public void AlarmClock_RecurringSchedule_HasCronAndNextFireAt()
    {
        var alarm = new AlarmClock
        {
            Id = "a3",
            Name = "Recurring",
            TargetEntity = "media_player.bedroom",
            CronSchedule = "0 7 * * 1-5",
            NextFireAt = DateTimeOffset.UtcNow.AddDays(1)
        };

        Assert.NotNull(alarm.CronSchedule);
        Assert.NotNull(alarm.NextFireAt);
    }

    // -- AlarmSound model tests --

    [Fact]
    public void AlarmSound_Defaults_AreCorrect()
    {
        var sound = new AlarmSound
        {
            Id = "s1",
            Name = "Gentle",
            MediaSourceUri = "media-source://local/gentle.wav"
        };

        Assert.False(sound.UploadedViaLucia);
        Assert.False(sound.IsDefault);
    }

    [Fact]
    public void AlarmSound_DefaultMarkedCorrectly()
    {
        var sound = new AlarmSound
        {
            Id = "s2",
            Name = "Default Alarm",
            MediaSourceUri = "media-source://local/default.wav",
            IsDefault = true,
            UploadedViaLucia = true
        };

        Assert.True(sound.IsDefault);
        Assert.True(sound.UploadedViaLucia);
    }

    // -- Presence routing tests --

    [Fact]
    public async Task ExecuteAsync_PresenceTarget_ResolvesToOccupiedRoomMediaPlayer()
    {
        var task = CreateAlarmTask(
            targetEntity: "presence",
            alarmSoundUri: "media-source://local/alarm.wav",
            playbackInterval: TimeSpan.FromSeconds(1),
            autoDismissAfter: TimeSpan.FromMilliseconds(300));

        var haClient = A.Fake<IHomeAssistantClient>();
        var presenceService = A.Fake<IPresenceDetectionService>();
        var entityLocationService = A.Fake<IEntityLocationService>();

        A.CallTo(() => presenceService.GetOccupiedAreasAsync(A<CancellationToken>._))
            .Returns(new List<OccupiedArea>
            {
                new("bedroom", "Bedroom", true, 1, PresenceConfidence.Highest),
                new("kitchen", "Kitchen", false, 0, PresenceConfidence.Medium)
            });

        A.CallTo(() => entityLocationService.FindEntitiesByLocationAsync(
            "bedroom", A<IReadOnlyList<string>>._, A<CancellationToken>._))
            .Returns(new List<EntityLocationInfo>
            {
                new() { EntityId = "media_player.bedroom_speaker", FriendlyName = "Bedroom Speaker", AreaId = "bedroom" }
            });

        var sp = BuildPresenceServiceProvider(haClient, presenceService, entityLocationService);
        await task.ExecuteAsync(sp, CancellationToken.None);

        // Should have played on the bedroom speaker, not "presence"
        A.CallTo(() => haClient.CallServiceAsync(
            "media_player",
            "play_media",
            A<string?>._,
            A<ServiceCallRequest?>.That.Matches(r =>
                r != null && r.EntityId == "media_player.bedroom_speaker"),
            A<CancellationToken>._)).MustHaveHappened();
    }

    [Fact]
    public async Task ExecuteAsync_PresenceTarget_PicksHighestConfidenceArea()
    {
        var task = CreateAlarmTask(
            targetEntity: "presence",
            alarmSoundUri: "media-source://local/alarm.wav",
            playbackInterval: TimeSpan.FromSeconds(1),
            autoDismissAfter: TimeSpan.FromMilliseconds(300));

        var haClient = A.Fake<IHomeAssistantClient>();
        var presenceService = A.Fake<IPresenceDetectionService>();
        var entityLocationService = A.Fake<IEntityLocationService>();

        A.CallTo(() => presenceService.GetOccupiedAreasAsync(A<CancellationToken>._))
            .Returns(new List<OccupiedArea>
            {
                new("kitchen", "Kitchen", true, 0, PresenceConfidence.Low),
                new("office", "Office", true, 2, PresenceConfidence.Highest)
            });

        A.CallTo(() => entityLocationService.FindEntitiesByLocationAsync(
            "office", A<IReadOnlyList<string>>._, A<CancellationToken>._))
            .Returns(new List<EntityLocationInfo>
            {
                new() { EntityId = "media_player.office_speaker", FriendlyName = "Office Speaker", AreaId = "office" }
            });

        var sp = BuildPresenceServiceProvider(haClient, presenceService, entityLocationService);
        await task.ExecuteAsync(sp, CancellationToken.None);

        A.CallTo(() => haClient.CallServiceAsync(
            "media_player",
            "play_media",
            A<string?>._,
            A<ServiceCallRequest?>.That.Matches(r =>
                r != null && r.EntityId == "media_player.office_speaker"),
            A<CancellationToken>._)).MustHaveHappened();
    }

    [Fact]
    public async Task ExecuteAsync_PresenceTarget_NoOccupiedArea_SkipsPlayback()
    {
        var task = CreateAlarmTask(
            targetEntity: "presence",
            alarmSoundUri: "media-source://local/alarm.wav",
            playbackInterval: TimeSpan.FromSeconds(1),
            autoDismissAfter: TimeSpan.FromMilliseconds(200));

        var haClient = A.Fake<IHomeAssistantClient>();
        var presenceService = A.Fake<IPresenceDetectionService>();
        var entityLocationService = A.Fake<IEntityLocationService>();

        A.CallTo(() => presenceService.GetOccupiedAreasAsync(A<CancellationToken>._))
            .Returns(new List<OccupiedArea>());

        var sp = BuildPresenceServiceProvider(haClient, presenceService, entityLocationService);
        await task.ExecuteAsync(sp, CancellationToken.None);

        // Should not have played anything — no occupied area found
        A.CallTo(() => haClient.CallServiceAsync(
            A<string>._,
            A<string>._,
            A<string?>._,
            A<ServiceCallRequest?>._,
            A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task ExecuteAsync_PresenceTarget_NoMediaPlayerInArea_SkipsPlayback()
    {
        var task = CreateAlarmTask(
            targetEntity: "presence",
            alarmSoundUri: "media-source://local/alarm.wav",
            playbackInterval: TimeSpan.FromSeconds(1),
            autoDismissAfter: TimeSpan.FromMilliseconds(200));

        var haClient = A.Fake<IHomeAssistantClient>();
        var presenceService = A.Fake<IPresenceDetectionService>();
        var entityLocationService = A.Fake<IEntityLocationService>();

        A.CallTo(() => presenceService.GetOccupiedAreasAsync(A<CancellationToken>._))
            .Returns(new List<OccupiedArea>
            {
                new("garage", "Garage", true, 1, PresenceConfidence.High)
            });

        A.CallTo(() => entityLocationService.FindEntitiesByLocationAsync(
            "garage", A<IReadOnlyList<string>>._, A<CancellationToken>._))
            .Returns(new List<EntityLocationInfo>());

        var sp = BuildPresenceServiceProvider(haClient, presenceService, entityLocationService);
        await task.ExecuteAsync(sp, CancellationToken.None);

        // No media_player in area — should not play
        A.CallTo(() => haClient.CallServiceAsync(
            A<string>._,
            A<string>._,
            A<string?>._,
            A<ServiceCallRequest?>._,
            A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task ExecuteAsync_DirectTarget_DoesNotQueryPresenceService()
    {
        var task = CreateAlarmTask(
            targetEntity: "media_player.bedroom",
            alarmSoundUri: "media-source://local/alarm.wav",
            playbackInterval: TimeSpan.FromSeconds(1),
            autoDismissAfter: TimeSpan.FromMilliseconds(200));

        var haClient = A.Fake<IHomeAssistantClient>();
        var presenceService = A.Fake<IPresenceDetectionService>();
        var entityLocationService = A.Fake<IEntityLocationService>();

        var sp = BuildPresenceServiceProvider(haClient, presenceService, entityLocationService);
        await task.ExecuteAsync(sp, CancellationToken.None);

        // Direct target — presence should never be queried
        A.CallTo(() => presenceService.GetOccupiedAreasAsync(A<CancellationToken>._))
            .MustNotHaveHappened();

        // But playback should still happen
        A.CallTo(() => haClient.CallServiceAsync(
            "media_player",
            "play_media",
            A<string?>._,
            A<ServiceCallRequest?>._,
            A<CancellationToken>._)).MustHaveHappened();
    }

    // -- Helpers --

    private static AlarmScheduledTask CreateAlarmTask(
        string id = "alarm-1",
        string targetEntity = "media_player.bedroom",
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
            TargetEntity = targetEntity,
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

    private static IServiceProvider BuildPresenceServiceProvider(
        IHomeAssistantClient haClient,
        IPresenceDetectionService presenceService,
        IEntityLocationService entityLocationService)
    {
        var sc = new ServiceCollection();
        sc.AddSingleton(haClient);
        sc.AddSingleton(presenceService);
        sc.AddSingleton(entityLocationService);
        sc.AddSingleton(A.Fake<ILogger<AlarmScheduledTask>>());
        return sc.BuildServiceProvider();
    }
}
