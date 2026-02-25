namespace lucia.TimerAgent.ScheduledTasks;

/// <summary>
/// A named alarm sound — persisted in MongoDB "alarm_sounds" collection.
/// Maps a user-friendly name to a media-source:// URI on the Home Assistant instance.
/// </summary>
public sealed class AlarmSound
{
    /// <summary>Unique ID (MongoDB _id).</summary>
    public required string Id { get; init; }

    /// <summary>User-facing name (e.g., "Gentle", "Radar", "Classic").</summary>
    public required string Name { get; set; }

    /// <summary>
    /// Home Assistant media-source URI
    /// (e.g., "media-source://media_source/local/alarms/gentle.wav").
    /// </summary>
    public required string MediaSourceUri { get; set; }

    /// <summary>
    /// Whether this sound was uploaded via Lucia dashboard (true)
    /// or mapped from existing HA media library (false).
    /// When true, deleting this sound also removes the file from HA.
    /// </summary>
    public bool UploadedViaLucia { get; set; }

    /// <summary>Whether this is the default alarm sound when none is specified.</summary>
    public bool IsDefault { get; set; }

    /// <summary>When the sound mapping was created.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
