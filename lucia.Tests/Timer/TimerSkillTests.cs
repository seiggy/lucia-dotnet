using FakeItEasy;
using lucia.Agents.Abstractions;
using lucia.Agents.Models;
using lucia.Agents.Models.HomeAssistant;
using lucia.Agents.Services;
using lucia.HomeAssistant.Models;
using lucia.HomeAssistant.Services;
using lucia.TimerAgent;
using lucia.TimerAgent.ScheduledTasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace lucia.Tests.Timer;

/// <summary>
/// Unit tests for <see cref="TimerSkill"/>.
/// </summary>
public sealed class TimerSkillTests
{
    private readonly IEntityLocationService _entityLocationService = A.Fake<IEntityLocationService>(
        options => options.Implements<IAgentFilteredEntityLocationService>());
    private IAgentFilteredEntityLocationService FilteredLocations => (IAgentFilteredEntityLocationService)_entityLocationService;
    private readonly IHomeAssistantClient _haClient = A.Fake<IHomeAssistantClient>();
    private readonly ScheduledTaskStore _taskStore = new();
    private readonly IScheduledTaskRepository _taskRepository = A.Fake<IScheduledTaskRepository>();
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly ILogger<TimerSkill> _logger = A.Fake<ILogger<TimerSkill>>();
    private readonly TimerSkill _skill;

    public TimerSkillTests()
    {
        _timeProvider.SetUtcNow(new DateTimeOffset(2025, 7, 15, 12, 0, 0, TimeSpan.Zero));
        _skill = new TimerSkill(_entityLocationService, _haClient, _taskStore, _taskRepository, _timeProvider, _logger);
    }

    private static HomeAssistantEntity MakeSatellite(string entityId) =>
        new() { EntityId = entityId, FriendlyName = entityId.Split('.')[1] };

    [Fact]
    public async Task LocationResolution_DoesNotFallBackToUnscopedInventory()
    {
        await _skill.SetTimerAsync(60, "Done!", "office");
        A.CallTo(() => _entityLocationService.FindEntitiesByLocationAsync(
            A<string>._, A<IReadOnlyList<string>?>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task LocationResolution_WithoutFilteredService_DoesNotCreateTimer()
    {
        var unscoped = A.Fake<IEntityLocationService>();
        var skill = new TimerSkill(unscoped, _haClient, _taskStore, _taskRepository, _timeProvider, _logger);
        var result = await skill.SetTimerAsync(60, "Done!", "office");
        Assert.Contains("Could not find", result);
        Assert.Equal(0, skill.ActiveTimerCount);
        A.CallTo(() => unscoped.FindEntitiesByLocationAsync(A<string>._,
            A<IReadOnlyList<string>?>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    private static HomeAssistantState StateWithFeatures(string entityId, int supportedFeatures) =>
        new()
        {
            EntityId = entityId,
            Attributes = new Dictionary<string, object> { ["supported_features"] = supportedFeatures }
        };

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

        Assert.Equal(1, _taskStore.Count);
        var tasks = _taskStore.GetByType(ScheduledTaskType.Timer);
        var timer = Assert.IsType<TimerScheduledTask>(tasks.First());
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
        Assert.Equal(1, _taskStore.Count);

        await _skill.CancelTimerAsync(timerId);

        Assert.Equal(0, _taskStore.Count);
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

    [Fact]
    public async Task SetTimerAsync_PersistsToMongoDb()
    {
        await _skill.SetTimerAsync(120, "Persist me", "assist_satellite.kitchen");

        A.CallTo(() => _taskRepository.UpsertAsync(
            A<ScheduledTaskDocument>.That.Matches(d =>
                d.TaskType == ScheduledTaskType.Timer &&
                d.Message == "Persist me" &&
                d.EntityId == "assist_satellite.kitchen" &&
                d.DurationSeconds == 120),
            A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task CancelTimerAsync_UpdatesMongoDbStatus()
    {
        var setResult = await _skill.SetTimerAsync(300, "Cancel me", "assist_satellite.kitchen");
        var timerId = ExtractTimerId(setResult);

        await _skill.CancelTimerAsync(timerId);

        A.CallTo(() => _taskRepository.UpdateStatusAsync(
            timerId,
            ScheduledTaskStatus.Cancelled,
            A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task SetTimerAsync_PropagatesCallerToken_ToRepository()
    {
        using var cts = new CancellationTokenSource();

        await _skill.SetTimerAsync(120, "Token flows", "assist_satellite.kitchen", cts.Token);

        A.CallTo(() => _taskRepository.UpsertAsync(
            A<ScheduledTaskDocument>._,
            A<CancellationToken>.That.IsEqualTo(cts.Token)))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task SetTimerAsync_PropagatesCallerToken_ToEntityLocationService()
    {
        using var cts = new CancellationTokenSource();

        // A bare location name (no dot) forces resolution via the entity location service.
        await _skill.SetTimerAsync(120, "Locate me", "office", cts.Token);

        A.CallTo(() => FilteredLocations.FindEntitiesByLocationForAgentAsync(
            "office", "timer-agent",
            A<IReadOnlyList<string>?>._, A<HybridMatchOptions?>._,
            A<CancellationToken>.That.IsEqualTo(cts.Token)))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task SetTimerAsync_LocationName_SatelliteSupportsAnnounce_ResolvedFromLiveState()
    {
        // The entity registry never carries supported_features, so capability must come from state.
        A.CallTo(() => FilteredLocations.FindEntitiesByLocationForAgentAsync(
                "office", "timer-agent", A<IReadOnlyList<string>?>._, A<HybridMatchOptions?>._, A<CancellationToken>._))
            .Returns(Task.FromResult<IReadOnlyList<HomeAssistantEntity>>(
                [MakeSatellite("assist_satellite.office_sat")]));

        A.CallTo(() => _haClient.GetStateAsync("assist_satellite.office_sat", A<CancellationToken>._))
            .Returns(Task.FromResult<HomeAssistantState?>(StateWithFeatures("assist_satellite.office_sat", 3)));

        var result = await _skill.SetTimerAsync(120, "Done!", "office");

        Assert.Contains("set for", result);
        A.CallTo(() => _taskRepository.UpsertAsync(
                A<ScheduledTaskDocument>.That.Matches(d => d.EntityId == "assist_satellite.office_sat"),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task SetTimerAsync_LocationName_SatelliteLacksAnnounce_ReturnsNotFound()
    {
        // features=2 (StartConversation only) — no Announce bit.
        A.CallTo(() => FilteredLocations.FindEntitiesByLocationForAgentAsync(
                "office", "timer-agent", A<IReadOnlyList<string>?>._, A<HybridMatchOptions?>._, A<CancellationToken>._))
            .Returns(Task.FromResult<IReadOnlyList<HomeAssistantEntity>>(
                [MakeSatellite("assist_satellite.office_sat")]));

        A.CallTo(() => _haClient.GetStateAsync("assist_satellite.office_sat", A<CancellationToken>._))
            .Returns(Task.FromResult<HomeAssistantState?>(StateWithFeatures("assist_satellite.office_sat", 2)));

        var result = await _skill.SetTimerAsync(120, "Done!", "office");

        Assert.Contains("Could not find an assist_satellite device", result);
        Assert.Equal(0, _skill.ActiveTimerCount);
    }

    [Fact]
    public async Task SetTimerAsync_LocationName_StateUnavailable_FallsBackToFirstSatellite()
    {
        // HA state fetch fails (e.g. transient network error) — fail open rather than reject.
        A.CallTo(() => FilteredLocations.FindEntitiesByLocationForAgentAsync(
                "office", "timer-agent", A<IReadOnlyList<string>?>._, A<HybridMatchOptions?>._, A<CancellationToken>._))
            .Returns(Task.FromResult<IReadOnlyList<HomeAssistantEntity>>(
                [MakeSatellite("assist_satellite.office_sat")]));

        A.CallTo(() => _haClient.GetStateAsync("assist_satellite.office_sat", A<CancellationToken>._))
            .Throws<HttpRequestException>();

        var result = await _skill.SetTimerAsync(120, "Done!", "office");

        Assert.Contains("set for", result);
        A.CallTo(() => _taskRepository.UpsertAsync(
                A<ScheduledTaskDocument>.That.Matches(d => d.EntityId == "assist_satellite.office_sat"),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task SetTimerAsync_LocationName_PrefersAnnounceCapableOverFirstCandidate()
    {
        A.CallTo(() => FilteredLocations.FindEntitiesByLocationForAgentAsync(
                "office", "timer-agent", A<IReadOnlyList<string>?>._, A<HybridMatchOptions?>._, A<CancellationToken>._))
            .Returns(Task.FromResult<IReadOnlyList<HomeAssistantEntity>>(
            [
                MakeSatellite("assist_satellite.office_incapable"),
                MakeSatellite("assist_satellite.office_capable")
            ]));

        A.CallTo(() => _haClient.GetStateAsync("assist_satellite.office_incapable", A<CancellationToken>._))
            .Returns(Task.FromResult<HomeAssistantState?>(StateWithFeatures("assist_satellite.office_incapable", 2)));
        A.CallTo(() => _haClient.GetStateAsync("assist_satellite.office_capable", A<CancellationToken>._))
            .Returns(Task.FromResult<HomeAssistantState?>(StateWithFeatures("assist_satellite.office_capable", 3)));

        await _skill.SetTimerAsync(120, "Done!", "office");

        A.CallTo(() => _taskRepository.UpsertAsync(
                A<ScheduledTaskDocument>.That.Matches(d => d.EntityId == "assist_satellite.office_capable"),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task SetTimerAsync_LocationName_PrefersUnverifiedOverVerifiedIncapable()
    {
        // Candidate A is confirmed incapable; candidate B's state is unreadable.
        // An unverified satellite in the right area beats a known-bad one.
        A.CallTo(() => FilteredLocations.FindEntitiesByLocationForAgentAsync(
                "office", "timer-agent", A<IReadOnlyList<string>?>._, A<HybridMatchOptions?>._, A<CancellationToken>._))
            .Returns(Task.FromResult<IReadOnlyList<HomeAssistantEntity>>(
            [
                MakeSatellite("assist_satellite.office_incapable"),
                MakeSatellite("assist_satellite.office_unknown")
            ]));

        A.CallTo(() => _haClient.GetStateAsync("assist_satellite.office_incapable", A<CancellationToken>._))
            .Returns(Task.FromResult<HomeAssistantState?>(StateWithFeatures("assist_satellite.office_incapable", 0)));
        A.CallTo(() => _haClient.GetStateAsync("assist_satellite.office_unknown", A<CancellationToken>._))
            .Throws<HttpRequestException>();

        await _skill.SetTimerAsync(120, "Done!", "office");

        A.CallTo(() => _taskRepository.UpsertAsync(
                A<ScheduledTaskDocument>.That.Matches(d => d.EntityId == "assist_satellite.office_unknown"),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task SetTimerAsync_PreCancelledToken_ThrowsWithoutPersistingOrScheduling()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _skill.SetTimerAsync(120, "Never fires", "assist_satellite.kitchen", cts.Token));

        A.CallTo(() => _taskRepository.UpsertAsync(A<ScheduledTaskDocument>._, A<CancellationToken>._))
            .MustNotHaveHappened();
        Assert.Equal(0, _skill.ActiveTimerCount);
    }

    [Fact]
    public async Task SetTimerAsync_CallerCancelled_RepositoryOce_IsNotSwallowed()
    {
        // Cancel from inside UpsertAsync so the entry ThrowIfCancellationRequested guard is
        // bypassed and the catch/rethrow branch in SetTimerAsync is actually exercised.
        using var cts = new CancellationTokenSource();

        A.CallTo(() => _taskRepository.UpsertAsync(A<ScheduledTaskDocument>._, A<CancellationToken>._))
            .ReturnsLazily(call =>
            {
                cts.Cancel();
                return Task.FromException(new OperationCanceledException(cts.Token));
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _skill.SetTimerAsync(120, "Cancel me", "assist_satellite.kitchen", cts.Token));

        A.CallTo(() => _taskRepository.UpsertAsync(A<ScheduledTaskDocument>._, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        // Caller cancellation must abort -- the timer is not silently created in-memory.
        Assert.Equal(0, _skill.ActiveTimerCount);
    }

    [Fact]
    public async Task SetTimerAsync_NonCallerOce_IsSwallowed_TimerRunsInMemory()
    {
        // A repository OCE that is NOT the caller's cancellation (e.g. Mongo internal timeout)
        // must be treated as a persistence failure and fall back to the in-memory timer.
        A.CallTo(() => _taskRepository.UpsertAsync(A<ScheduledTaskDocument>._, A<CancellationToken>._))
            .ThrowsAsync(new OperationCanceledException());

        var result = await _skill.SetTimerAsync(120, "Fallback", "assist_satellite.kitchen", CancellationToken.None);

        Assert.Contains("set for", result);
        Assert.Equal(1, _skill.ActiveTimerCount);
    }

    [Fact]
    public async Task CancelTimerAsync_PropagatesCallerToken_ToRepository()
    {
        var setResult = await _skill.SetTimerAsync(300, "Cancel me", "assist_satellite.kitchen");
        var timerId = ExtractTimerId(setResult);
        using var cts = new CancellationTokenSource();

        await _skill.CancelTimerAsync(timerId, cts.Token);

        A.CallTo(() => _taskRepository.UpdateStatusAsync(
            timerId,
            ScheduledTaskStatus.Cancelled,
            A<CancellationToken>.That.IsEqualTo(cts.Token)))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task CancelTimerAsync_CallerCancelled_DuringStatusUpdate_ConvergesAndLeavesTimerRemoved()
    {
        // Arrange: the caller's token fires inside UpdateStatusAsync. The first write may commit
        // Cancelled after the OCE surfaces (late-commit scenario). The compensation must write
        // the same terminal Cancelled with an uncancellable token so both the original and the
        // compensation converge on Cancelled regardless of ordering -- no ghost timer fires.
        var setResult = await _skill.SetTimerAsync(300, "Converge me", "assist_satellite.kitchen");
        var timerId = ExtractTimerId(setResult);
        using var cts = new CancellationTokenSource();

        A.CallTo(() => _taskRepository.UpdateStatusAsync(
                timerId, ScheduledTaskStatus.Cancelled, A<CancellationToken>.That.Matches(ct => ct.CanBeCanceled)))
            .ReturnsLazily(_ =>
            {
                cts.Cancel();
                return Task.FromException(new OperationCanceledException(cts.Token));
            });

        A.CallTo(() => _taskRepository.UpdateStatusAsync(
                timerId, ScheduledTaskStatus.Cancelled, A<CancellationToken>.That.Matches(ct => !ct.CanBeCanceled)))
            .Returns(Task.CompletedTask);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _skill.CancelTimerAsync(timerId, cts.Token));

        // Compensation must have written terminal Cancelled with a non-cancellable token.
        A.CallTo(() => _taskRepository.UpdateStatusAsync(
                timerId, ScheduledTaskStatus.Cancelled, A<CancellationToken>.That.Matches(ct => !ct.CanBeCanceled)))
            .MustHaveHappenedOnceExactly();

        // Timer must stay removed regardless of whether the first write committed.
        Assert.Equal(0, _skill.ActiveTimerCount);
    }

    [Fact]
    public async Task ListTimers_CallerCancelled_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => _skill.ListTimers(cts.Token));
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
