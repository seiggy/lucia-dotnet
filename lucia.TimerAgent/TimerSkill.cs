using System.Collections.Concurrent;
using System.Diagnostics;
using lucia.HomeAssistant.Models;
using lucia.HomeAssistant.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace lucia.TimerAgent;

/// <summary>
/// Skill that creates timed announcements on Home Assistant assist satellite devices.
/// When a timer expires, it calls assist_satellite.announce on the originating device.
/// </summary>
public sealed class TimerSkill
{
    private static readonly ActivitySource ActivitySource = new("Lucia.Skills.Timer", "1.0.0");

    private readonly IHomeAssistantClient _haClient;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<TimerSkill> _logger;
    private readonly ConcurrentDictionary<string, ActiveTimer> _activeTimers = new();

    public TimerSkill(
        IHomeAssistantClient haClient,
        TimeProvider timeProvider,
        ILogger<TimerSkill> logger)
    {
        _haClient = haClient ?? throw new ArgumentNullException(nameof(haClient));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Returns the AI tool definitions for the timer skill.
    /// </summary>
    public IList<AITool> GetTools()
    {
        return
        [
            AIFunctionFactory.Create(SetTimerAsync, new AIFunctionFactoryOptions
            {
                Name = "SetTimer",
                Description = """
                    Creates a timer that will announce a message on the user's satellite device when it expires.
                    Use this when the user asks to set a timer, reminder, or alarm for a specific duration.
                    The durationSeconds parameter is the number of seconds until the timer fires.
                    The message parameter is a friendly announcement message to play when the timer expires (e.g. "Your pizza timer is done!").
                    The entityId parameter is the Home Assistant entity_id of the assist_satellite device to announce on.
                    """
            }),
            AIFunctionFactory.Create(CancelTimerAsync, new AIFunctionFactoryOptions
            {
                Name = "CancelTimer",
                Description = "Cancels an active timer by its ID. Returns whether the cancellation was successful."
            }),
            AIFunctionFactory.Create(ListTimers, new AIFunctionFactoryOptions
            {
                Name = "ListTimers",
                Description = "Lists all currently active timers with their remaining time and message."
            })
        ];
    }

    /// <summary>
    /// Sets a timer that will play a TTS announcement on the specified satellite device when it expires.
    /// </summary>
    public async Task<string> SetTimerAsync(int durationSeconds, string message, string entityId)
    {
        using var activity = ActivitySource.StartActivity("TimerSkill.SetTimer", ActivityKind.Internal);

        if (durationSeconds <= 0)
        {
            return "Timer duration must be greater than zero seconds.";
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            return "Timer message cannot be empty.";
        }

        if (string.IsNullOrWhiteSpace(entityId))
        {
            return "Entity ID for the satellite device is required.";
        }

        var timerId = Guid.NewGuid().ToString("N")[..8];
        var expiresAt = _timeProvider.GetUtcNow().AddSeconds(durationSeconds);

        activity?.SetTag("timer.id", timerId);
        activity?.SetTag("timer.duration_seconds", durationSeconds);
        activity?.SetTag("timer.entity_id", entityId);

        var cts = new CancellationTokenSource();
        var timer = new ActiveTimer
        {
            Id = timerId,
            Message = message,
            EntityId = entityId,
            DurationSeconds = durationSeconds,
            ExpiresAt = expiresAt,
            Cts = cts
        };

        _activeTimers[timerId] = timer;

        _logger.LogInformation(
            "Timer {TimerId} set for {Duration}s on {EntityId}: {Message}",
            timerId, durationSeconds, entityId, message);

        // Fire the background task; don't await — it runs independently
        _ = RunTimerAsync(timer);

        var friendlyDuration = FormatDuration(TimeSpan.FromSeconds(durationSeconds));
        return $"Timer '{timerId}' set for {friendlyDuration}. I'll announce \"{message}\" when it's done.";
    }

    /// <summary>
    /// Cancels an active timer.
    /// </summary>
    public Task<string> CancelTimerAsync(string timerId)
    {
        if (_activeTimers.TryRemove(timerId, out var timer))
        {
            timer.Cts.Cancel();
            timer.Cts.Dispose();
            _logger.LogInformation("Timer {TimerId} cancelled", timerId);
            return Task.FromResult($"Timer '{timerId}' has been cancelled.");
        }

        return Task.FromResult($"No active timer found with ID '{timerId}'.");
    }

    /// <summary>
    /// Lists all active timers.
    /// </summary>
    public Task<string> ListTimers()
    {
        if (_activeTimers.IsEmpty)
        {
            return Task.FromResult("No active timers.");
        }

        var now = _timeProvider.GetUtcNow();
        var lines = _activeTimers.Values
            .OrderBy(t => t.ExpiresAt)
            .Select(t =>
            {
                var remaining = t.ExpiresAt - now;
                var friendlyRemaining = remaining.TotalSeconds > 0
                    ? FormatDuration(remaining)
                    : "expiring now";
                return $"- Timer '{t.Id}': {friendlyRemaining} remaining — \"{t.Message}\" → {t.EntityId}";
            });

        return Task.FromResult($"Active timers:\n{string.Join('\n', lines)}");
    }

    /// <summary>
    /// Gets the count of currently active timers (for testing).
    /// </summary>
    internal int ActiveTimerCount => _activeTimers.Count;

    /// <summary>
    /// Runs the timer delay then calls Home Assistant to announce.
    /// </summary>
    internal async Task RunTimerAsync(ActiveTimer timer)
    {
        try
        {
            var delay = timer.ExpiresAt - _timeProvider.GetUtcNow();
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, _timeProvider, timer.Cts.Token).ConfigureAwait(false);
            }

            await AnnounceAsync(timer.EntityId, timer.Message, timer.Cts.Token).ConfigureAwait(false);

            _logger.LogInformation("Timer {TimerId} fired successfully on {EntityId}", timer.Id, timer.EntityId);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Timer {TimerId} was cancelled", timer.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Timer {TimerId} failed to announce on {EntityId}", timer.Id, timer.EntityId);
        }
        finally
        {
            _activeTimers.TryRemove(timer.Id, out _);
            timer.Cts.Dispose();
        }
    }

    /// <summary>
    /// Calls the Home Assistant assist_satellite.announce service.
    /// </summary>
    internal async Task AnnounceAsync(string entityId, string message, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("TimerSkill.Announce", ActivityKind.Client);
        activity?.SetTag("ha.domain", "assist_satellite");
        activity?.SetTag("ha.service", "announce");
        activity?.SetTag("ha.entity_id", entityId);

        var request = new ServiceCallRequest
        {
            EntityId = entityId,
            ["message"] = message
        };

        await _haClient.CallServiceAsync(
            "assist_satellite",
            "announce",
            parameters: null,
            request: request,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static string FormatDuration(TimeSpan ts)
    {
        if (ts.TotalHours >= 1)
        {
            return ts.Minutes > 0
                ? $"{(int)ts.TotalHours} hour(s) and {ts.Minutes} minute(s)"
                : $"{(int)ts.TotalHours} hour(s)";
        }

        if (ts.TotalMinutes >= 1)
        {
            return ts.Seconds > 0
                ? $"{(int)ts.TotalMinutes} minute(s) and {ts.Seconds} second(s)"
                : $"{(int)ts.TotalMinutes} minute(s)";
        }

        return $"{(int)ts.TotalSeconds} second(s)";
    }
}
