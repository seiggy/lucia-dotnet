using System.Text.Json.Serialization;

namespace lucia.HomeAssistant.Models;

/// <summary>
/// An area entry from the Home Assistant config registry.
/// Retrieved via Jinja template using the areas() and floor_areas() template functions.
/// </summary>
public sealed class AreaRegistryEntry
{
    [JsonPropertyName("area_id")]
    public string AreaId { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("floor_id")]
    public string? FloorId { get; set; }

    [JsonPropertyName("aliases")]
    public List<string> Aliases { get; set; } = [];

    [JsonPropertyName("icon")]
    public string? Icon { get; set; }

    [JsonPropertyName("labels")]
    public List<string> Labels { get; set; } = [];
}
