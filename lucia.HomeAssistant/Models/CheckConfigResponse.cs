using System.Text.Json.Serialization;

namespace lucia.HomeAssistant.Models;

/// <summary>
/// Configuration validation result.
/// </summary>
public sealed record CheckConfigResponse
{
    [JsonPropertyName("errors")]
    public string? Errors { get; init; }

    [JsonPropertyName("result")]
    public required string Result { get; init; }
}
