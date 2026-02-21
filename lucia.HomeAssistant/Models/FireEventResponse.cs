using System.Text.Json.Serialization;

namespace lucia.HomeAssistant.Models;

/// <summary>
/// Response from firing an event.
/// </summary>
public sealed record FireEventResponse
{
    [JsonPropertyName("message")]
    public required string Message { get; init; }
}
