using FakeItEasy;
using lucia.TimerAgent.ScheduledTasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace lucia.Tests.ScheduledTasks;

public sealed class CronScheduleServiceTests
{
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly ILogger<CronScheduleService> _logger = A.Fake<ILogger<CronScheduleService>>();
    private readonly CronScheduleService _service;

    public CronScheduleServiceTests()
    {
        // Fix time to Wednesday 2025-07-16 08:00:00 UTC
        _timeProvider.SetUtcNow(new DateTimeOffset(2025, 7, 16, 8, 0, 0, TimeSpan.Zero));
        _service = new CronScheduleService(_timeProvider, _logger);
    }

    // --- IsValid ---

    [Theory]
    [InlineData("0 7 * * *", true)]       // Daily at 7:00 AM
    [InlineData("0 7 * * 1-5", true)]     // Weekdays at 7:00 AM
    [InlineData("30 6 * * 0,6", true)]    // Weekends at 6:30 AM
    [InlineData("*/15 * * * *", true)]     // Every 15 minutes
    [InlineData("invalid", false)]
    [InlineData("", false)]
    [InlineData("0 25 * * *", false)]      // Invalid hour
    [InlineData("60 7 * * *", false)]      // Invalid minute
    public void IsValid_ReturnsExpected(string cronExpression, bool expected)
    {
        Assert.Equal(expected, _service.IsValid(cronExpression));
    }

    // --- GetNextOccurrence ---

    [Fact]
    public void GetNextOccurrence_DailyAt7AM_ReturnsNextDay()
    {
        // Current time: Wed 2025-07-16 08:00 UTC → 7:00 AM already passed today
        var next = _service.GetNextOccurrence("0 7 * * *");

        Assert.NotNull(next);
        Assert.Equal(new DateTimeOffset(2025, 7, 17, 7, 0, 0, TimeSpan.Zero), next.Value);
    }

    [Fact]
    public void GetNextOccurrence_DailyAt9AM_ReturnsSameDay()
    {
        // Current time: Wed 2025-07-16 08:00 UTC → 9:00 AM hasn't happened yet
        var next = _service.GetNextOccurrence("0 9 * * *");

        Assert.NotNull(next);
        Assert.Equal(new DateTimeOffset(2025, 7, 16, 9, 0, 0, TimeSpan.Zero), next.Value);
    }

    [Fact]
    public void GetNextOccurrence_WeekdaysAt7AM_SkipsWeekend()
    {
        // Advance to Friday 2025-07-18 08:00 UTC
        _timeProvider.SetUtcNow(new DateTimeOffset(2025, 7, 18, 8, 0, 0, TimeSpan.Zero));

        var next = _service.GetNextOccurrence("0 7 * * 1-5");

        Assert.NotNull(next);
        // Next weekday after Friday 08:00 with 07:00 passed → Monday 2025-07-21
        Assert.Equal(new DateTimeOffset(2025, 7, 21, 7, 0, 0, TimeSpan.Zero), next.Value);
    }

    [Fact]
    public void GetNextOccurrence_WeekendsAt630AM_SkipsWeekdays()
    {
        // Current time: Wed 2025-07-16 08:00 UTC
        var next = _service.GetNextOccurrence("30 6 * * 0,6");

        Assert.NotNull(next);
        // Next weekend: Saturday 2025-07-19
        Assert.Equal(new DateTimeOffset(2025, 7, 19, 6, 30, 0, TimeSpan.Zero), next.Value);
    }

    [Fact]
    public void GetNextOccurrence_FromSpecificTime_ComputesRelativeToThat()
    {
        var from = new DateTimeOffset(2025, 12, 31, 23, 0, 0, TimeSpan.Zero);
        var next = _service.GetNextOccurrence("0 7 * * *", from);

        Assert.NotNull(next);
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 7, 0, 0, TimeSpan.Zero), next.Value);
    }

    [Fact]
    public void GetNextOccurrence_InvalidExpression_ReturnsNull()
    {
        var next = _service.GetNextOccurrence("invalid cron");
        Assert.Null(next);
    }

    // --- AdvanceSchedule ---

    [Fact]
    public void AdvanceSchedule_CronAlarm_SetsNextFireAt()
    {
        var alarm = new AlarmClock
        {
            Id = "a1",
            Name = "Morning",
            TargetEntity = "media_player.bedroom",
            CronSchedule = "0 7 * * *",
            NextFireAt = new DateTimeOffset(2025, 7, 16, 7, 0, 0, TimeSpan.Zero),
            IsEnabled = true
        };

        var result = _service.AdvanceSchedule(alarm);

        Assert.True(result);
        Assert.True(alarm.IsEnabled);
        Assert.NotNull(alarm.NextFireAt);
        // Should be tomorrow at 7:00 AM (current time 08:00, so 07:00 today has passed)
        Assert.Equal(new DateTimeOffset(2025, 7, 17, 7, 0, 0, TimeSpan.Zero), alarm.NextFireAt.Value);
    }

    [Fact]
    public void AdvanceSchedule_OneShotAlarm_DeactivatesAlarm()
    {
        var alarm = new AlarmClock
        {
            Id = "a2",
            Name = "Nap",
            TargetEntity = "media_player.bedroom",
            CronSchedule = null,
            NextFireAt = new DateTimeOffset(2025, 7, 16, 14, 0, 0, TimeSpan.Zero),
            IsEnabled = true
        };

        var result = _service.AdvanceSchedule(alarm);

        Assert.False(result);
        Assert.False(alarm.IsEnabled);
        Assert.Null(alarm.NextFireAt);
    }

    [Fact]
    public void AdvanceSchedule_InvalidCron_DeactivatesAlarm()
    {
        var alarm = new AlarmClock
        {
            Id = "a3",
            Name = "Bad alarm",
            TargetEntity = "media_player.bedroom",
            CronSchedule = "invalid",
            NextFireAt = _timeProvider.GetUtcNow(),
            IsEnabled = true
        };

        var result = _service.AdvanceSchedule(alarm);

        Assert.False(result);
        Assert.False(alarm.IsEnabled);
        Assert.Null(alarm.NextFireAt);
    }

    // --- InitializeNextFireAt ---

    [Fact]
    public void InitializeNextFireAt_CronAlarm_SetsNextFireAt()
    {
        var alarm = new AlarmClock
        {
            Id = "a4",
            Name = "Daily alarm",
            TargetEntity = "media_player.bedroom",
            CronSchedule = "0 7 * * *",
            NextFireAt = null,
            IsEnabled = true
        };

        _service.InitializeNextFireAt(alarm);

        Assert.NotNull(alarm.NextFireAt);
        Assert.Equal(new DateTimeOffset(2025, 7, 17, 7, 0, 0, TimeSpan.Zero), alarm.NextFireAt.Value);
    }

    [Fact]
    public void InitializeNextFireAt_OneShotAlarm_DoesNothing()
    {
        var alarm = new AlarmClock
        {
            Id = "a5",
            Name = "One-shot",
            TargetEntity = "media_player.bedroom",
            CronSchedule = null,
            NextFireAt = new DateTimeOffset(2025, 7, 20, 9, 0, 0, TimeSpan.Zero),
            IsEnabled = true
        };

        _service.InitializeNextFireAt(alarm);

        // Should remain unchanged — one-shot alarms have manually-set NextFireAt
        Assert.Equal(new DateTimeOffset(2025, 7, 20, 9, 0, 0, TimeSpan.Zero), alarm.NextFireAt);
    }

    // --- Describe ---

    [Theory]
    [InlineData("0 7 * * *", "Daily at 7:00 AM")]
    [InlineData("0 7 * * 1-5", "Weekdays at 7:00 AM")]
    [InlineData("0 7 * * 0,6", "Weekends at 7:00 AM")]
    [InlineData("30 14 * * *", "Daily at 2:30 PM")]
    [InlineData("invalid", "Invalid schedule")]
    public void Describe_ReturnsExpectedDescription(string cronExpression, string expected)
    {
        Assert.Equal(expected, CronScheduleService.Describe(cronExpression));
    }
}
