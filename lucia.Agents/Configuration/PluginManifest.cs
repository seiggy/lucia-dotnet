using System.Text.Json.Serialization;

namespace lucia.Agents.Configuration;

/// <summary>
/// Root model for a <c>lucia-plugins.json</c> repository manifest.
/// Each plugin repository publishes this file listing available plugins.
/// The same format is used whether served from GitHub or read from the local filesystem.
/// </summary>
public sealed class PluginManifest
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("plugins")]
    public List<PluginManifestEntry> Plugins { get; set; } = [];
}
