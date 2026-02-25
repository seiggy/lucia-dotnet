using System.Diagnostics;
using lucia.HomeAssistant.Models;
using lucia.HomeAssistant.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace lucia.TimerAgent.ScheduledTasks;

/// <summary>
/// A scheduled task that announces a TTS message when it fires.
/// This is the <see cref="IScheduledTask"/> equivalent of <see cref="ActiveTimer"/>.
/// </summary>
public sealed class TimerScheduledTask : IScheduledTask
{
    private static readonly ActivitySource ActivitySource = new("Lucia.ScheduledTasks.Timer", "1.0.0");

    public required string Id { get; init; }
    public required string TaskId { get; init; }
    public required string Label { get; init; }
    public required DateTimeOffset FireAt { get; init; }
    public ScheduledTaskType TaskType => ScheduledTaskType.Timer;

    /// <summary>
    /// TTS message to announce on the target entity.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Target <c>assist_satellite</c> entity for TTS announcement.
    /// </summary>
    public required string EntityId { get; init; }

    /// <summary>
    /// Original timer duration in seconds (for display purposes).
    /// </summary>
    public required int DurationSeconds { get; init; }

    public bool IsExpired(DateTimeOffset now) => FireAt <= now;

    public async Task ExecuteAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("TimerTask.Announce", ActivityKind.Client);
        activity?.SetTag("ha.domain", "assist_satellite");
        activity?.SetTag("ha.service", "announce");
        activity?.SetTag("ha.entity_id", EntityId);

        var logger = services.GetRequiredService<ILogger<TimerScheduledTask>>();

        logger.LogInformation(
            "Timer {TimerId} expired — announcing on {EntityId}: {Message}",
            Id, EntityId, Message);

        await using var scope = services.CreateAsyncScope();
        var haClient = scope.ServiceProvider.GetRequiredService<IHomeAssistantClient>();

        var request = new ServiceCallRequest
        {
            EntityId = EntityId,
            ["message"] = Message
        };

        await haClient.CallServiceAsync(
            "assist_satellite",
            "announce",
            parameters: null,
            request: request,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Timer {TimerId} fired successfully on {EntityId}",
            Id, EntityId);
    }

    public ScheduledTaskDocument ToDocument() => new()
    {
        Id = Id,
        TaskId = TaskId,
        Label = Label,
        FireAt = FireAt,
        TaskType = ScheduledTaskType.Timer,
        Status = ScheduledTaskStatus.Pending,
        Message = Message,
        EntityId = EntityId,
        DurationSeconds = DurationSeconds
    };
}
