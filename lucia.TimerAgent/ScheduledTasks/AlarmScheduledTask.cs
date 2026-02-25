using System.Diagnostics;
using lucia.Agents.Services;
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

    /// <summary>Starting volume for volume ramping (0.0–1.0). Null = no ramping.</summary>
    public double? VolumeStart { get; init; }

    /// <summary>Target volume for volume ramping (0.0–1.0). Null = no ramping.</summary>
    public double? VolumeEnd { get; init; }

    /// <summary>Duration over which volume ramps from VolumeStart to VolumeEnd.</summary>
    public TimeSpan VolumeRampDuration { get; init; } = TimeSpan.FromSeconds(30);

    public bool IsExpired(DateTimeOffset now) => FireAt <= now;

    public async Task ExecuteAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("AlarmTask.Ring", ActivityKind.Client);
        activity?.SetTag("alarm.id", Id);
        activity?.SetTag("alarm.clock_id", AlarmClockId);
        activity?.SetTag("alarm.target_entity", TargetEntity);
        activity?.SetTag("alarm.has_sound", AlarmSoundUri is not null);

        var logger = services.GetRequiredService<ILogger<AlarmScheduledTask>>();

        // Resolve the actual playback entity — "presence" is resolved at fire time
        var resolvedEntity = await ResolveTargetEntityAsync(services, logger, cancellationToken).ConfigureAwait(false);
        if (resolvedEntity is null)
        {
            logger.LogWarning("Alarm {AlarmId} — could not resolve target entity, skipping", Id);
            return;
        }

        activity?.SetTag("alarm.resolved_entity", resolvedEntity);

        logger.LogInformation(
            "Alarm {AlarmId} firing — ringing on {TargetEntity} for up to {AutoDismiss}",
            Id, resolvedEntity, AutoDismissAfter);

        await using var scope = services.CreateAsyncScope();
        var haClient = scope.ServiceProvider.GetRequiredService<IHomeAssistantClient>();

        // Use a linked CancellationToken that auto-cancels after AutoDismissAfter
        using var autoDismissCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        autoDismissCts.CancelAfter(AutoDismissAfter);

        try
        {
            await PlayAlarmLoopAsync(haClient, resolvedEntity, logger, autoDismissCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Auto-dismiss timeout — not an error
            logger.LogInformation(
                "Alarm {AlarmId} auto-dismissed after {Timeout}",
                Id, AutoDismissAfter);
        }
    }

    /// <summary>
    /// Resolves the target entity for playback. When <see cref="TargetEntity"/> is "presence",
    /// queries <see cref="IPresenceDetectionService"/> to find an occupied room, then finds
    /// a media_player in that room via <see cref="IEntityLocationService"/>.
    /// Falls back to the configured TargetEntity if presence resolution fails.
    /// </summary>
    private async Task<string?> ResolveTargetEntityAsync(
        IServiceProvider services,
        ILogger logger,
        CancellationToken ct)
    {
        if (!TargetEntity.Equals("presence", StringComparison.OrdinalIgnoreCase))
            return TargetEntity;

        var presenceService = services.GetService<IPresenceDetectionService>();
        if (presenceService is null)
        {
            logger.LogWarning("Alarm {AlarmId} targets 'presence' but IPresenceDetectionService is not registered", Id);
            return null;
        }

        var entityLocationService = services.GetService<IEntityLocationService>();
        if (entityLocationService is null)
        {
            logger.LogWarning("Alarm {AlarmId} targets 'presence' but IEntityLocationService is not registered", Id);
            return null;
        }

        var occupiedAreas = await presenceService.GetOccupiedAreasAsync(ct).ConfigureAwait(false);
        var bestArea = occupiedAreas
            .Where(a => a.IsOccupied)
            .OrderByDescending(a => a.Confidence)
            .ThenByDescending(a => a.OccupantCount ?? 0)
            .FirstOrDefault();

        if (bestArea is null)
        {
            logger.LogWarning("Alarm {AlarmId} targets 'presence' but no occupied area found", Id);
            return null;
        }

        logger.LogInformation(
            "Alarm {AlarmId} presence resolved to area '{AreaName}' (confidence={Confidence})",
            Id, bestArea.AreaName, bestArea.Confidence);

        // Find a media_player in the occupied area
        var mediaPlayers = await entityLocationService.FindEntitiesByLocationAsync(
            bestArea.AreaId,
            domainFilter: ["media_player"],
            ct: ct).ConfigureAwait(false);

        if (mediaPlayers.Count == 0)
        {
            logger.LogWarning(
                "Alarm {AlarmId} — area '{AreaName}' is occupied but has no media_player entities",
                Id, bestArea.AreaName);
            return null;
        }

        var selected = mediaPlayers[0].EntityId;
        logger.LogInformation(
            "Alarm {AlarmId} — selected media_player '{EntityId}' in '{AreaName}'",
            Id, selected, bestArea.AreaName);
        return selected;
    }

    private async Task PlayAlarmLoopAsync(
        IHomeAssistantClient haClient,
        string resolvedEntity,
        ILogger logger,
        CancellationToken ct)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var hasVolumeRamp = VolumeStart.HasValue && VolumeEnd.HasValue && VolumeStart < VolumeEnd;

        // Set initial volume before first play
        if (hasVolumeRamp)
        {
            await SetVolumeAsync(haClient, resolvedEntity, VolumeStart!.Value, logger, ct).ConfigureAwait(false);
        }

        while (!ct.IsCancellationRequested)
        {
            // Ramp volume before each playback iteration
            if (hasVolumeRamp)
            {
                var elapsed = DateTimeOffset.UtcNow - startedAt;
                var progress = Math.Clamp(elapsed / VolumeRampDuration, 0.0, 1.0);
                var volume = VolumeStart!.Value + (VolumeEnd!.Value - VolumeStart.Value) * progress;
                await SetVolumeAsync(haClient, resolvedEntity, volume, logger, ct).ConfigureAwait(false);
            }

            try
            {
                if (AlarmSoundUri is not null)
                {
                    await PlayMediaAsync(haClient, resolvedEntity, ct).ConfigureAwait(false);
                }
                else
                {
                    await AnnounceFallbackAsync(haClient, resolvedEntity, ct).ConfigureAwait(false);
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
    /// Sets the volume on a media_player entity.
    /// </summary>
    private static async Task SetVolumeAsync(
        IHomeAssistantClient haClient,
        string entity,
        double volume,
        ILogger logger,
        CancellationToken ct)
    {
        try
        {
            var request = new ServiceCallRequest
            {
                EntityId = entity,
                ["volume_level"] = Math.Round(volume, 2)
            };

            await haClient.CallServiceAsync(
                "media_player",
                "volume_set",
                parameters: null,
                request: request,
                cancellationToken: ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to set volume to {Volume} on {Entity}", volume, entity);
        }
    }

    /// <summary>
    /// Plays alarm sound via <c>media_player.play_media</c> with <c>announce: true</c>.
    /// </summary>
    private async Task PlayMediaAsync(IHomeAssistantClient haClient, string entity, CancellationToken ct)
    {
        var request = new ServiceCallRequest
        {
            EntityId = entity,
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
    private async Task AnnounceFallbackAsync(IHomeAssistantClient haClient, string entity, CancellationToken ct)
    {
        var request = new ServiceCallRequest
        {
            EntityId = entity,
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
        AutoDismissAfter = AutoDismissAfter,
        VolumeStart = VolumeStart,
        VolumeEnd = VolumeEnd,
        VolumeRampDuration = VolumeRampDuration
    };
}
