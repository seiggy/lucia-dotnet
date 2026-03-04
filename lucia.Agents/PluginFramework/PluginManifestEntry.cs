using System.Text.Json.Serialization;

namespace lucia.Agents.PluginFramework;

/// <summary>
/// A single plugin entry in a <c>lucia-plugins.json</c> repository manifest.
/// </summary>
public sealed class PluginManifestEntry
{
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("author")]
    public string? Author { get; set; }

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];

    /// <summary>
    /// Relative path to the plugin folder within the repository (e.g. "plugins/metamcp").
    /// For git repositories this is used to build download URLs.
    /// For local repositories this is resolved against the local directory.
    /// </summary>
    [JsonPropertyName("path")]
    public required string Path { get; set; }

    [JsonPropertyName("homepage")]
    public string? Homepage { get; set; }

    [JsonPropertyName("minLuciaVersion")]
    public string? MinLuciaVersion { get; set; }
}
