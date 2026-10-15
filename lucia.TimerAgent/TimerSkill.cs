using System.Diagnostics;
using lucia.Agents.Abstractions;
using lucia.Agents.Models.HomeAssistant;
using lucia.Agents.Services;
using lucia.HomeAssistant.Models;
using lucia.HomeAssistant.Services;
using lucia.TimerAgent.ScheduledTasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace lucia.TimerAgent;

/// <summary>
/// Skill that creates timed announcements on Home Assistant assist satellite devices.
/// Timers are added to <see cref="ScheduledTaskStore"/> and executed by <see cref="ScheduledTaskService"/>.
/// Timers are persisted via <see cref="IScheduledTaskRepository"/> for durability across restarts.
/// </summary>
public sealed class TimerSkill
{
    private static readonly ActivitySource ActivitySource = new("Lucia.Skills.Timer", "1.0.0");

    private readonly IEntityLocationService _entityLocationService;
    private readonly IHomeAssistantClient _homeAssistantClient;
    private readonly ScheduledTaskStore _taskStore;
    private readonly IScheduledTaskRepository _taskRepository;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<TimerSkill> _logger;

    public TimerSkill(
        IEntityLocationService entityLocationService,
        IHomeAssistantClient homeAssistantClient,
        ScheduledTaskStore taskStore,
        IScheduledTaskRepository taskRepository,
        TimeProvider timeProvider,
        ILogger<TimerSkill> logger)
    {
        _entityLocationService = entityLocationService ?? throw new ArgumentNullException(nameof(entityLocationService));
        _homeAssistantClient = homeAssistantClient ?? throw new ArgumentNullException(nameof(homeAssistantClient));
        _taskStore = taskStore ?? throw new ArgumentNullException(nameof(taskStore));
        _taskRepository = taskRepository ?? throw new ArgumentNullException(nameof(taskRepository));
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
                    The entityId parameter is the location name or area where the announcement should play (e.g. "office", "bedroom").
                    It can also be a full Home Assistant entity_id (e.g. "assist_satellite.my_satellite").
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
    /// The timer is added to <see cref="ScheduledTaskStore"/> and executed by <see cref="ScheduledTaskService"/>.
    /// </summary>
    public async Task<string> SetTimerAsync(int durationSeconds, string message, string entityId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
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
            return "Entity ID or location for the satellite device is required.";
        }

        // Resolve location name to an actual assist_satellite entity_id
        var resolvedEntityId = await ResolveSatelliteEntityAsync(entityId, cancellationToken).ConfigureAwait(false);
        if (resolvedEntityId is null)
        {
            return $"Could not find an assist_satellite device in '{entityId}'. Available satellites can be found in areas with voice assistant devices.";
        }

        _logger.LogInformation("Resolved satellite '{Input}' to entity '{EntityId}'", entityId, resolvedEntityId);

        var timerId = Guid.NewGuid().ToString("N")[..8];
        var taskId = Guid.NewGuid().ToString("N");
        var fireAt = _timeProvider.GetUtcNow().AddSeconds(durationSeconds);

        activity?.SetTag("timer.id", timerId);
        activity?.SetTag("timer.task_id", taskId);
        activity?.SetTag("timer.duration_seconds", durationSeconds);
        activity?.SetTag("timer.entity_id", resolvedEntityId);

        var timerTask = new TimerScheduledTask
        {
            Id = timerId,
            TaskId = taskId,
            Label = $"Timer: {message}",
            FireAt = fireAt,
            Message = message,
            EntityId = resolvedEntityId,
            DurationSeconds = durationSeconds
        };

        // Persist for crash recovery
        try
        {
            await _taskRepository.UpsertAsync(timerTask.ToDocument(), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist timer {TimerId} to the database — timer will still run in-memory", timerId);
        }

        _taskStore.Add(timerTask);

        _logger.LogInformation(
            "Timer {TimerId} (task {TaskId}) set for {Duration}s on {EntityId}: {Message}",
            timerId, taskId, durationSeconds, resolvedEntityId, message);

        var friendlyDuration = FormatDuration(TimeSpan.FromSeconds(durationSeconds));
        return $"Timer '{timerId}' set for {friendlyDuration}. I'll announce \"{message}\" when it's done.";
    }

    /// <summary>
    /// Cancels an active timer.
    /// </summary>
    public async Task<string> CancelTimerAsync(string timerId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_taskStore.TryRemove(timerId, out _))
        {
            try
            {
                await _taskRepository.UpdateStatusAsync(timerId, ScheduledTaskStatus.Cancelled, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The caller cancelled while the first write was in-flight. That write may commit
                // Cancelled after the OCE surfaces (late-commit). Writing the same terminal state
                // with an uncancellable token is idempotent: both the original write and this
                // compensation converge on Cancelled regardless of ordering, so the in-memory
                // removal cannot reintroduce a live timer.
                try
                {
                    await _taskRepository.UpdateStatusAsync(timerId, ScheduledTaskStatus.Cancelled, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception compensationEx)
                {
                    _logger.LogWarning(compensationEx, "Failed to converge task {TimerId} to Cancelled after caller cancellation; durable recovery will reconcile on restart", timerId);
                }
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update task {TimerId} status to Cancelled in the database", timerId);
            }

            _logger.LogInformation("Timer {TimerId} cancelled", timerId);
            return $"Timer '{timerId}' has been cancelled.";
        }

        return $"No active timer found with ID '{timerId}'.";
    }

    /// <summary>
    /// Lists all active timers.
    /// </summary>
    public Task<string> ListTimers(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var timers = _taskStore.GetByType(ScheduledTaskType.Timer);
        if (timers.Count == 0)
        {
            return Task.FromResult("No active timers.");
        }

        var now = _timeProvider.GetUtcNow();
        var lines = timers
            .OrderBy(t => t.FireAt)
            .Select(t =>
            {
                var remaining = t.FireAt - now;
                var friendlyRemaining = remaining.TotalSeconds > 0
                    ? FormatDuration(remaining)
                    : "expiring now";
                var timerTask = t as TimerScheduledTask;
                var msg = timerTask?.Message ?? t.Label;
                var entity = timerTask?.EntityId ?? "unknown";
                return $"- Timer '{t.Id}': {friendlyRemaining} remaining — \"{msg}\" → {entity}";
            });

        return Task.FromResult($"Active timers:\n{string.Join('\n', lines)}");
    }

    /// <summary>
    /// Gets the count of currently active timers (for testing).
    /// </summary>
    public int ActiveTimerCount => _taskStore.GetByType(ScheduledTaskType.Timer).Count;

    /// <summary>
    /// Resolves a location name or entity_id to an actual assist_satellite entity_id.
    /// If the input already looks like a valid entity_id (contains a dot), returns it as-is.
    /// Otherwise, searches the entity location service for assist_satellite entities in that area.
    /// Announce capability is verified against each candidate's live state — Home Assistant's
    /// entity registry does not carry <c>supported_features</c>, so the registry value (always 0)
    /// cannot be used. Candidates whose state cannot be read are treated as unverified and
    /// preferred over candidates confirmed to lack the announce capability.
    /// </summary>
    private async Task<string?> ResolveSatelliteEntityAsync(string input, CancellationToken cancellationToken)
    {
        // Already a full entity_id — use as-is
        if (input.Contains('.'))
        {
            return input;
        }

        // Search for assist_satellite entities in the requested location
        var entities = await _entityLocationService.FindEntitiesByLocationAsync(
            input,
            domainFilter: ["assist_satellite"],
            ct: cancellationToken).ConfigureAwait(false);

        if (entities.Count == 0)
        {
            _logger.LogWarning("No assist_satellite entity found for location '{Location}'", input);
            return null;
        }

        var candidates = new List<(HomeAssistantEntity Entity, int Features, bool Verified)>();
        foreach (var entity in entities)
        {
            try
            {
                var state = await _homeAssistantClient.GetStateAsync(entity.EntityId, cancellationToken).ConfigureAwait(false);
                if (state is not null
                    && state.Attributes.TryGetValue("supported_features", out var featObj)
                    && int.TryParse(featObj?.ToString(), out var feat))
                {
                    candidates.Add((entity, feat, true));
                }
                else
                {
                    candidates.Add((entity, 0, false));
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read state for satellite '{EntityId}' — treating as unverified", entity.EntityId);
                candidates.Add((entity, 0, false));
            }
        }

        var entityDetails = string.Join(", ", candidates.Select(c =>
            c.Verified
                ? $"{c.Entity.EntityId} (features={c.Features})"
                : $"{c.Entity.EntityId} (features={c.Features}, unverified)"));

        _logger.LogDebug(
            "Found {Count} satellite(s) in '{Location}': {Entities}",
            entities.Count, input, entityDetails);

        var announceable = candidates
            .Where(c => c.Verified && ((SupportedFeaturesFlags)c.Features).HasFlag(SupportedFeaturesFlags.Announce))
            .OrderByDescending(c => c.Features)
            .Select(c => c.Entity.EntityId)
            .ToList();

        if (announceable.Count > 0)
        {
            return announceable[0];
        }

        if (candidates.All(c => c.Verified))
        {
            _logger.LogWarning(
                "Found {Count} satellite(s) in '{Location}' but none support announce",
                entities.Count, input);
            return null;
        }

        // At least one candidate's capability could not be verified. Fail open: an assist_satellite
        // in the requested area is a better target than no target at all — if it truly cannot
        // announce, the failure surfaces when the timer fires.
        var unverified = candidates.First(c => !c.Verified).Entity.EntityId;
        _logger.LogWarning(
            "Found {Count} satellite(s) in '{Location}' but could not verify announce capability — using '{EntityId}'",
            entities.Count, input, unverified);
        return unverified;
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
