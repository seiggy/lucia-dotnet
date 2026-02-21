using System.Text.Json.Serialization;

namespace lucia.HomeAssistant.Models;

/// <summary>
/// Event type with listener count.
/// </summary>
public sealed record EventInfo
{
    [JsonPropertyName("event")]
    public required string Event { get; init; }

    [JsonPropertyName("listener_count")]
    public required int ListenerCount { get; init; }
}
