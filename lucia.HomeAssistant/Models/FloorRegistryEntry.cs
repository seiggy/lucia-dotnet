using System.Text.Json.Serialization;

namespace lucia.HomeAssistant.Models;

/// <summary>
/// A floor entry from the Home Assistant config registry.
/// Retrieved via WebSocket command config/floor_registry/list.
/// </summary>
public sealed class FloorRegistryEntry
{
    [JsonPropertyName("floor_id")]
    public string FloorId { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("aliases")]
    public List<string> Aliases { get; set; } = [];

    [JsonPropertyName("level")]
    public int? Level { get; set; }

    [JsonPropertyName("icon")]
    public string? Icon { get; set; }
}
