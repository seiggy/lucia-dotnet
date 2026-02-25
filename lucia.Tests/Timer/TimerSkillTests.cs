using A2A;
using FakeItEasy;
using lucia.Agents.Services;
using lucia.HomeAssistant.Models;
using lucia.HomeAssistant.Services;
using lucia.TimerAgent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace lucia.Tests.Timer;

/// <summary>
/// Unit tests for <see cref="TimerSkill"/>.
/// </summary>
public sealed class TimerSkillTests
{
    private readonly IEntityLocationService _entityLocationService = A.Fake<IEntityLocationService>();
    private readonly ITaskStore _taskStore = A.Fake<ITaskStore>();
    private readonly ActiveTimerStore _timerStore = new();
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly ILogger<TimerSkill> _logger = A.Fake<ILogger<TimerSkill>>();
    private readonly TimerSkill _skill;

    public TimerSkillTests()
    {
        _timeProvider.SetUtcNow(new DateTimeOffset(2025, 7, 15, 12, 0, 0, TimeSpan.Zero));
        _skill = new TimerSkill(_entityLocationService, _taskStore, _timerStore, _timeProvider, _logger);
    }

    [Fact]
    public async Task SetTimerAsync_ValidInput_CreatesTimerAndReturnsConfirmation()
    {
        var result = await _skill.SetTimerAsync(300, "Pizza is ready!", "assist_satellite.kitchen");

        Assert.Contains("set for", result);
        Assert.Contains("5 minute(s)", result);
        Assert.Contains("Pizza is ready!", result);
        Assert.Equal(1, _skill.ActiveTimerCount);
    }

    [Fact]
    public async Task SetTimerAsync_ZeroDuration_ReturnsError()
    {
        var result = await _skill.SetTimerAsync(0, "Test", "assist_satellite.kitchen");

        Assert.Equal("Timer duration must be greater than zero seconds.", result);
        Assert.Equal(0, _skill.ActiveTimerCount);
    }

    [Fact]
    public async Task SetTimerAsync_NegativeDuration_ReturnsError()
    {
        var result = await _skill.SetTimerAsync(-10, "Test", "assist_satellite.kitchen");

        Assert.Equal("Timer duration must be greater than zero seconds.", result);
        Assert.Equal(0, _skill.ActiveTimerCount);
    }

    [Fact]
    public async Task SetTimerAsync_EmptyMessage_ReturnsError()
    {
        var result = await _skill.SetTimerAsync(60, "", "assist_satellite.kitchen");

        Assert.Equal("Timer message cannot be empty.", result);
        Assert.Equal(0, _skill.ActiveTimerCount);
    }

    [Fact]
    public async Task SetTimerAsync_EmptyEntityId_ReturnsError()
    {
        var result = await _skill.SetTimerAsync(60, "Test", "");

        Assert.Equal("Entity ID or location for the satellite device is required.", result);
        Assert.Equal(0, _skill.ActiveTimerCount);
    }

    [Fact]
    public async Task SetTimerAsync_ValidInput_AddsTimerToStore()
    {
        await _skill.SetTimerAsync(60, "Timer done!", "assist_satellite.office");

        Assert.Equal(1, _timerStore.Count);
        var timers = _timerStore.GetAll();
        var timer = timers.First();
        Assert.Equal("assist_satellite.office", timer.EntityId);
        Assert.Equal("Timer done!", timer.Message);
        Assert.Equal(60, timer.DurationSeconds);
    }

    [Fact]
    public async Task CancelTimerAsync_ActiveTimer_CancelsAndReturnsSuccess()
    {
        var setResult = await _skill.SetTimerAsync(300, "Test", "assist_satellite.kitchen");
        var timerId = ExtractTimerId(setResult);

        var cancelResult = await _skill.CancelTimerAsync(timerId);

        Assert.Contains("has been cancelled", cancelResult);
        Assert.Equal(0, _skill.ActiveTimerCount);
    }

    [Fact]
    public async Task CancelTimerAsync_NonExistentTimer_ReturnsNotFound()
    {
        var result = await _skill.CancelTimerAsync("nonexistent");

        Assert.Contains("No active timer found", result);
    }

    [Fact]
    public async Task CancelTimerAsync_RemovesTimerFromStore()
    {
        var setResult = await _skill.SetTimerAsync(60, "Cancelled!", "assist_satellite.office");
        var timerId = ExtractTimerId(setResult);
        Assert.Equal(1, _timerStore.Count);

        await _skill.CancelTimerAsync(timerId);

        Assert.Equal(0, _timerStore.Count);
    }

    [Fact]
    public async Task ListTimers_NoTimers_ReturnsEmpty()
    {
        var result = await _skill.ListTimers();

        Assert.Equal("No active timers.", result);
    }

    [Fact]
    public async Task ListTimers_WithActiveTimers_ReturnsFormattedList()
    {
        await _skill.SetTimerAsync(300, "Timer 1", "assist_satellite.kitchen");
        await _skill.SetTimerAsync(600, "Timer 2", "assist_satellite.office");

        var result = await _skill.ListTimers();

        Assert.Contains("Active timers:", result);
        Assert.Contains("Timer 1", result);
        Assert.Contains("Timer 2", result);
        Assert.Contains("assist_satellite.kitchen", result);
        Assert.Contains("assist_satellite.office", result);
    }

    [Fact]
    public async Task SetTimerAsync_MultipleTimers_TracksAll()
    {
        await _skill.SetTimerAsync(60, "First", "assist_satellite.kitchen");
        await _skill.SetTimerAsync(120, "Second", "assist_satellite.office");
        await _skill.SetTimerAsync(180, "Third", "assist_satellite.bedroom");

        Assert.Equal(3, _skill.ActiveTimerCount);
    }

    [Fact]
    public async Task SetTimerAsync_HourDuration_FormatsCorrectly()
    {
        var result = await _skill.SetTimerAsync(3600, "Hour timer", "assist_satellite.kitchen");

        Assert.Contains("1 hour(s)", result);
    }

    [Fact]
    public async Task SetTimerAsync_SecondsOnlyDuration_FormatsCorrectly()
    {
        var result = await _skill.SetTimerAsync(45, "Short timer", "assist_satellite.kitchen");

        Assert.Contains("45 second(s)", result);
    }

    [Fact]
    public void GetTools_ReturnsThreeTools()
    {
        var tools = _skill.GetTools();

        Assert.Equal(3, tools.Count);
    }

    /// <summary>
    /// Extracts the timer ID from a SetTimer result string like "Timer 'abc12345' set for ...".
    /// </summary>
    private static string ExtractTimerId(string result)
    {
        var start = result.IndexOf('\'') + 1;
        var end = result.IndexOf('\'', start);
        return result[start..end];
    }
}
