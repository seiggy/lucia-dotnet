using MongoDB.Bson.Serialization.Attributes;

namespace lucia.TimerAgent.ScheduledTasks;

/// <summary>
/// A configured alarm clock definition — persisted in MongoDB "alarm_clocks" collection.
/// This is the alarm DEFINITION (what/when/where). Each firing produces an AlarmTask instance.
/// </summary>
public sealed class AlarmClock
{
    /// <summary>Unique ID (MongoDB _id).</summary>
    [BsonId]
    public required string Id { get; init; }

    /// <summary>User-facing name (e.g., "Morning Wake Up", "Nap Alarm").</summary>
    public required string Name { get; set; }

    /// <summary>
    /// Target entity for playback. Can be:
    /// - A specific media_player entity_id (e.g., "media_player.bedroom_satellite1_media_player")
    /// - The special value "presence" — plays on whichever speaker is in the room
    ///   where presence is currently detected (via IPresenceDetectionService)
    /// </summary>
    public required string TargetEntity { get; set; }

    /// <summary>
    /// Selected alarm sound — references an AlarmSound by Id.
    /// Null = fallback to TTS announce.
    /// </summary>
    public string? AlarmSoundId { get; set; }

    /// <summary>
    /// CRON expression defining the schedule (e.g., "0 7 * * 1-5" = 7:00 AM weekdays).
    /// Null = one-shot alarm (uses NextFireAt directly).
    /// When set, NextFireAt is ALWAYS computed from the CRON expression — never set manually.
    /// </summary>
    public string? CronSchedule { get; set; }

    /// <summary>
    /// Next scheduled fire time.
    /// - If CronSchedule is set: COMPUTED ONLY — always derived from CronSchedule. Never set manually.
    /// - If CronSchedule is null: SET MANUALLY — this is the one-shot fire time.
    /// After firing a CRON alarm, this is re-computed to the next occurrence.
    /// After firing a one-shot alarm, this is set to null (alarm becomes inactive).
    /// </summary>
    public DateTimeOffset? NextFireAt { get; set; }

    /// <summary>
    /// How often the alarm sound replays while ringing (e.g., every 30 seconds).
    /// The sound plays, waits this interval, then plays again until dismissed.
    /// </summary>
    public TimeSpan PlaybackInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long the alarm rings before auto-dismissing (safety net).
    /// E.g., 10 minutes — alarm stops even if nobody dismisses it.
    /// </summary>
    public TimeSpan AutoDismissAfter { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// When this alarm was last dismissed (by user or auto-dismiss).
    /// Used to determine if the alarm was dismissed before auto-dismiss kicked in,
    /// and to prevent re-firing if dismissed recently.
    /// </summary>
    public DateTimeOffset? LastDismissedAt { get; set; }

    /// <summary>Whether the alarm is currently enabled.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>When the alarm was created.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
