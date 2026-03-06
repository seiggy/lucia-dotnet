namespace lucia.Data.Models;

/// <summary>
/// Tracks whether presence detection is enabled and when it was last updated.
/// Extracted from the MongoDB-specific internal class for use with EF Core.
/// </summary>
public sealed class PresenceConfigEntry
{
    public const string EnabledKey = "presence_detection_enabled";

    public required string Key { get; init; }

    public bool Enabled { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
