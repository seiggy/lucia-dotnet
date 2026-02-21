using System.Text.Json.Serialization;

namespace lucia.HomeAssistant.Models;

/// <summary>
/// Calendar event with start/end times.
/// </summary>
public sealed record CalendarEvent
{
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("start")]
    public required CalendarDateTime Start { get; init; }

    [JsonPropertyName("end")]
    public required CalendarDateTime End { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("location")]
    public string? Location { get; init; }
}
