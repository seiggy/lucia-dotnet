using System.Text.Json.Serialization;

namespace lucia.HomeAssistant.Models;

/// <summary>
/// Request body for intent handling.
/// </summary>
public sealed record IntentRequest
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("data")]
    public Dictionary<string, object>? Data { get; init; }
}
