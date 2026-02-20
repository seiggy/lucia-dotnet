using System.Text.Json.Serialization;

namespace lucia.HomeAssistant.Models;

/// <summary>
/// Calendar entity summary.
/// </summary>
public sealed record CalendarEntity
{
    [JsonPropertyName("entity_id")]
    public required string EntityId { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }
}
