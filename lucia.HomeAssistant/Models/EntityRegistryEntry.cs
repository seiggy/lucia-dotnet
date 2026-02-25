using System.Text.Json.Serialization;

namespace lucia.HomeAssistant.Models;

/// <summary>
/// An entity entry from the Home Assistant config registry.
/// Retrieved via Jinja template using area_entities() and states template functions.
/// </summary>
public sealed class EntityRegistryEntry
{
    [JsonPropertyName("entity_id")]
    public string EntityId { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("original_name")]
    public string? OriginalName { get; set; }

    [JsonPropertyName("area_id")]
    public string? AreaId { get; set; }

    [JsonPropertyName("device_id")]
    public string? DeviceId { get; set; }

    [JsonPropertyName("aliases")]
    public List<string> Aliases { get; set; } = [];

    [JsonPropertyName("labels")]
    public List<string> Labels { get; set; } = [];

    [JsonPropertyName("disabled_by")]
    public string? DisabledBy { get; set; }

    [JsonPropertyName("hidden_by")]
    public string? HiddenBy { get; set; }

    [JsonPropertyName("platform")]
    public string? Platform { get; set; }

    [JsonPropertyName("supported_features")]
    public int SupportedFeatures { get; set; }
}
