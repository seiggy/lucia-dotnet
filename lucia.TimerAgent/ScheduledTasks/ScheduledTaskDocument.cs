using MongoDB.Bson.Serialization.Attributes;

namespace lucia.TimerAgent.ScheduledTasks;

/// <summary>
/// MongoDB-persistable representation of a scheduled task.
/// Used for persisting active tasks and recovering them on startup.
/// Stored in the "scheduled_tasks" collection.
/// </summary>
public sealed class ScheduledTaskDocument
{
    /// <summary>Unique task instance ID (MongoDB _id).</summary>
    [BsonId]
    public required string Id { get; init; }

    /// <summary>A2A task ID for external status tracking.</summary>
    public required string TaskId { get; init; }

    /// <summary>Human-readable description.</summary>
    public required string Label { get; init; }

    /// <summary>When the task should fire.</summary>
    public required DateTimeOffset FireAt { get; init; }

    /// <summary>Discriminator for deserializing the correct task type.</summary>
    public required ScheduledTaskType TaskType { get; init; }

    /// <summary>Current status of the task.</summary>
    public ScheduledTaskStatus Status { get; set; } = ScheduledTaskStatus.Pending;

    /// <summary>When the task was created.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>When the task was last fired (null if not yet fired).</summary>
    public DateTimeOffset? FiredAt { get; set; }

    /// <summary>When the task completed, was dismissed, or failed.</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    // --- TimerTask fields ---

    /// <summary>TTS message for timer announce.</summary>
    public string? Message { get; init; }

    /// <summary>Target entity for timer announce (assist_satellite entity ID).</summary>
    public string? EntityId { get; init; }

    /// <summary>Timer duration in seconds (for display/recovery).</summary>
    public int? DurationSeconds { get; init; }

    // --- AlarmTask fields ---

    /// <summary>Reference to the AlarmClock definition that spawned this task.</summary>
    public string? AlarmClockId { get; init; }

    /// <summary>Target media_player entity ID, or "presence" for presence-based routing.</summary>
    public string? TargetEntity { get; init; }

    /// <summary>media-source:// URI of the alarm sound to play.</summary>
    public string? AlarmSoundUri { get; init; }

    /// <summary>How often the alarm sound replays while ringing.</summary>
    public TimeSpan? PlaybackInterval { get; init; }

    /// <summary>How long the alarm rings before auto-dismissing.</summary>
    public TimeSpan? AutoDismissAfter { get; init; }

    /// <summary>Starting volume for volume ramping (0.0–1.0). Null = no ramping.</summary>
    public double? VolumeStart { get; init; }

    /// <summary>Target volume for volume ramping (0.0–1.0). Null = no ramping.</summary>
    public double? VolumeEnd { get; init; }

    /// <summary>Duration over which volume ramps from start to end.</summary>
    public TimeSpan? VolumeRampDuration { get; init; }

    // --- AgentTask fields ---

    /// <summary>The original user prompt to replay through the orchestrator.</summary>
    public string? Prompt { get; init; }

    /// <summary>Optional: the agent ID that should handle this (skip routing).</summary>
    public string? TargetAgentId { get; init; }

    /// <summary>Optional: captured entity context from the original request.</summary>
    public string? EntityContext { get; init; }
}
