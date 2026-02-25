using System.Diagnostics;
using A2A;
using lucia.HomeAssistant.Models;
using lucia.HomeAssistant.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace lucia.TimerAgent;

/// <summary>
/// Background service that polls active timers every second and fires announcements
/// when they expire. Owns its own <see cref="IHomeAssistantClient"/> scope so timer
/// execution is fully independent of the originating HTTP request lifecycle.
/// </summary>
public sealed class TimerExecutionService : BackgroundService
{
    private static readonly ActivitySource ActivitySource = new("Lucia.Skills.Timer", "1.0.0");

    private readonly ActiveTimerStore _timerStore;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ITaskStore _taskStore;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<TimerExecutionService> _logger;

    public TimerExecutionService(
        ActiveTimerStore timerStore,
        IServiceScopeFactory scopeFactory,
        ITaskStore taskStore,
        TimeProvider timeProvider,
        ILogger<TimerExecutionService> logger)
    {
        _timerStore = timerStore ?? throw new ArgumentNullException(nameof(timerStore));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _taskStore = taskStore ?? throw new ArgumentNullException(nameof(taskStore));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TimerExecutionService started — polling for expired timers");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessExpiredTimersAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Unexpected error in timer execution loop");
            }

            await Task.Delay(TimeSpan.FromSeconds(1), _timeProvider, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task ProcessExpiredTimersAsync(CancellationToken stoppingToken)
    {
        var now = _timeProvider.GetUtcNow();
        var timers = _timerStore.GetAll();

        foreach (var timer in timers)
        {
            if (stoppingToken.IsCancellationRequested)
                break;

            if (timer.ExpiresAt > now)
                continue;

            // Timer expired — remove from store and fire
            if (!_timerStore.TryRemove(timer.Id, out _))
                continue; // another thread already handled it

            _ = FireTimerAsync(timer, stoppingToken);
        }
    }

    private async Task FireTimerAsync(ActiveTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            _logger.LogInformation(
                "Timer {TimerId} (task {TaskId}) expired — announcing on {EntityId}",
                timer.Id, timer.TaskId, timer.EntityId);

            await AnnounceAsync(timer.EntityId, timer.Message, stoppingToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Timer {TimerId} (task {TaskId}) fired successfully on {EntityId}",
                timer.Id, timer.TaskId, timer.EntityId);

            try
            {
                var completedMessage = new AgentMessage
                {
                    MessageId = Guid.NewGuid().ToString("N"),
                    Role = MessageRole.Agent,
                    Parts = [new TextPart { Text = $"Timer fired: \"{timer.Message}\" announced on {timer.EntityId}" }]
                };
                await _taskStore.UpdateStatusAsync(timer.TaskId, TaskState.Completed, completedMessage, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update task {TaskId} status to Completed", timer.TaskId);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Timer {TimerId} (task {TaskId}) failed to announce on {EntityId}", timer.Id, timer.TaskId, timer.EntityId);

            try
            {
                var failedMessage = new AgentMessage
                {
                    MessageId = Guid.NewGuid().ToString("N"),
                    Role = MessageRole.Agent,
                    Parts = [new TextPart { Text = $"Timer failed: {ex.Message}" }]
                };
                await _taskStore.UpdateStatusAsync(timer.TaskId, TaskState.Failed, failedMessage, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception updateEx)
            {
                _logger.LogWarning(updateEx, "Failed to update task {TaskId} status to Failed", timer.TaskId);
            }
        }
        finally
        {
            timer.Cts.Dispose();
        }
    }

    /// <summary>
    /// Calls the Home Assistant assist_satellite.announce service using a scoped HA client.
    /// </summary>
    private async Task AnnounceAsync(string entityId, string message, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("TimerSkill.Announce", ActivityKind.Client);
        activity?.SetTag("ha.domain", "assist_satellite");
        activity?.SetTag("ha.service", "announce");
        activity?.SetTag("ha.entity_id", entityId);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var haClient = scope.ServiceProvider.GetRequiredService<IHomeAssistantClient>();

        var request = new ServiceCallRequest
        {
            EntityId = entityId,
            ["message"] = message
        };

        await haClient.CallServiceAsync(
            "assist_satellite",
            "announce",
            parameters: null,
            request: request,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
