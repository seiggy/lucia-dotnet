using System.Text.Json.Serialization;

namespace lucia.HomeAssistant.Models;

/// <summary>
/// Response from GET /api/
/// </summary>
public sealed record ApiStatusResponse
{
    [JsonPropertyName("message")]
    public required string Message { get; init; }
}
