using System.Text.Json.Serialization;

namespace lucia.HomeAssistant.Models;

/// <summary>
/// Polymorphic date/dateTime for calendar events.
/// Either <see cref="Date"/> (all-day) or <see cref="DateTime"/> (timed) will be set.
/// </summary>
public sealed record CalendarDateTime
{
    [JsonPropertyName("date")]
    public string? Date { get; init; }

    [JsonPropertyName("dateTime")]
    public string? DateTime { get; init; }
}
