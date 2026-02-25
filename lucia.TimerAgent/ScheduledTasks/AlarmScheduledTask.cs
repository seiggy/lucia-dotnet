using System.Diagnostics;
using lucia.HomeAssistant.Models;
using lucia.HomeAssistant.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace lucia.TimerAgent.ScheduledTasks;

/// <summary>
/// A scheduled task that plays an alarm sound on a media_player entity in a loop
/// until dismissed or the auto-dismiss timeout is reached.
///
/// Playback uses <c>media_player.play_media</c> with <c>announce: true</c> to
/// preserve current playback state (saves/resumes what was playing).
///
/// If no alarm sound URI is provided, falls back to TTS announce via
/// <c>assist_satellite.announce</c>.
/// </summary>
public sealed class AlarmScheduledTask : IScheduledTask
{
    private static readonly ActivitySource ActivitySource = new("Lucia.ScheduledTasks.Alarm", "1.0.0");

    public required string Id { get; init; }
    public required string TaskId { get; init; }
    public required string Label { get; init; }
    public required DateTimeOffset FireAt { get; init; }
    public ScheduledTaskType TaskType => ScheduledTaskType.Alarm;

    /// <summary>Reference to the AlarmClock definition that spawned this task.</summary>
    public required string AlarmClockId { get; init; }

    /// <summary>Target media_player entity ID for playback.</summary>
    public required string TargetEntity { get; init; }

    /// <summary>
    /// media-source:// URI of the alarm sound to play.
    /// Null = fallback to TTS announce "Alarm: {Label}".
    /// </summary>
    public string? AlarmSoundUri { get; init; }

    /// <summary>How often the alarm sound replays while ringing.</summary>
    public TimeSpan PlaybackInterval { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>How long the alarm rings before auto-dismissing.</summary>
    public TimeSpan AutoDismissAfter { get; init; } = TimeSpan.FromMinutes(10);

    public bool IsExpired(DateTimeOffset now) => FireAt <= now;

    public async Task ExecuteAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("AlarmTask.Ring", ActivityKind.Client);
        activity?.SetTag("alarm.id", Id);
        activity?.SetTag("alarm.clock_id", AlarmClockId);
        activity?.SetTag("alarm.target_entity", TargetEntity);
        activity?.SetTag("alarm.has_sound", AlarmSoundUri is not null);

        var logger = services.GetRequiredService<ILogger<AlarmScheduledTask>>();

        logger.LogInformation(
            "Alarm {AlarmId} firing — ringing on {TargetEntity} for up to {AutoDismiss}",
            Id, TargetEntity, AutoDismissAfter);

        await using var scope = services.CreateAsyncScope();
        var haClient = scope.ServiceProvider.GetRequiredService<IHomeAssistantClient>();

        // Use a linked CancellationToken that auto-cancels after AutoDismissAfter
        using var autoDismissCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        autoDismissCts.CancelAfter(AutoDismissAfter);

        try
        {
            await PlayAlarmLoopAsync(haClient, logger, autoDismissCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Auto-dismiss timeout — not an error
            logger.LogInformation(
                "Alarm {AlarmId} auto-dismissed after {Timeout}",
                Id, AutoDismissAfter);
        }
    }

    private async Task PlayAlarmLoopAsync(
        IHomeAssistantClient haClient,
        ILogger logger,
        CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (AlarmSoundUri is not null)
                {
                    await PlayMediaAsync(haClient, ct).ConfigureAwait(false);
                }
                else
                {
                    await AnnounceFallbackAsync(haClient, ct).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex,
                    "Alarm {AlarmId} playback iteration failed — retrying after interval",
                    Id);
            }

            await Task.Delay(PlaybackInterval, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Plays alarm sound via <c>media_player.play_media</c> with <c>announce: true</c>.
    /// </summary>
    private async Task PlayMediaAsync(IHomeAssistantClient haClient, CancellationToken ct)
    {
        var request = new ServiceCallRequest
        {
            EntityId = TargetEntity,
            ["media_content_id"] = AlarmSoundUri!,
            ["media_content_type"] = "music",
            ["announce"] = true
        };

        await haClient.CallServiceAsync(
            "media_player",
            "play_media",
            parameters: null,
            request: request,
            cancellationToken: ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Falls back to TTS announce when no alarm sound is configured.
    /// </summary>
    private async Task AnnounceFallbackAsync(IHomeAssistantClient haClient, CancellationToken ct)
    {
        var request = new ServiceCallRequest
        {
            EntityId = TargetEntity,
            ["message"] = $"Alarm: {Label}"
        };

        await haClient.CallServiceAsync(
            "assist_satellite",
            "announce",
            parameters: null,
            request: request,
            cancellationToken: ct).ConfigureAwait(false);
    }

    public ScheduledTaskDocument ToDocument() => new()
    {
        Id = Id,
        TaskId = TaskId,
        Label = Label,
        FireAt = FireAt,
        TaskType = ScheduledTaskType.Alarm,
        Status = ScheduledTaskStatus.Pending,
        AlarmClockId = AlarmClockId,
        TargetEntity = TargetEntity,
        AlarmSoundUri = AlarmSoundUri,
        PlaybackInterval = PlaybackInterval,
        AutoDismissAfter = AutoDismissAfter
    };
}
