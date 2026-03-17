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
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, object>? Data { get; init; }

    [JsonPropertyName("version")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Version { get; init; }

    [JsonPropertyName("data_length")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int DataLength { get; init; }

    [JsonPropertyName("payload_length")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int PayloadLength { get; init; }
}
