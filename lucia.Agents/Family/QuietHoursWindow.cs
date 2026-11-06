namespace lucia.Agents.Family;

/// <summary>
/// A daily window in which the member's conversational access is paused.
/// A window whose end is before its start spans midnight.
/// </summary>
public sealed class QuietHoursWindow
{
    public required TimeOnly Start { get; init; }

    public required TimeOnly End { get; init; }

    /// <summary>
    /// IANA or Windows time zone ID. Null uses the server's local time zone.
    /// </summary>
    public string? TimeZoneId { get; init; }
}
