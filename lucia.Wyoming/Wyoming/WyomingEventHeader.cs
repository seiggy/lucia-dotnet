namespace lucia.Wyoming.Wyoming;

using System.Text.Json.Serialization;

/// <summary>
/// Raw JSON header for Wyoming protocol messages.
/// </summary>
public sealed class WyomingEventHeader
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("data")]
    public Dictionary<string, object>? Data { get; init; }

    [JsonPropertyName("data_length")]
    public int DataLength { get; init; }

    [JsonPropertyName("payload_length")]
    public int PayloadLength { get; init; }
}
