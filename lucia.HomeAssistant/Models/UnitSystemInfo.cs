using System.Text.Json.Serialization;

namespace lucia.HomeAssistant.Models;

/// <summary>
/// Unit system configuration.
/// </summary>
public sealed record UnitSystemInfo
{
    [JsonPropertyName("length")]
    public required string Length { get; init; }

    [JsonPropertyName("mass")]
    public required string Mass { get; init; }

    [JsonPropertyName("temperature")]
    public required string Temperature { get; init; }

    [JsonPropertyName("volume")]
    public required string Volume { get; init; }
}
