using System.Text.Json.Serialization;
using lucia.Agents.Services;

namespace lucia.Agents.PluginFramework;

/// <summary>
/// Minimal DTO for deserializing a GitHub Release from the REST API.
/// Only includes fields needed for plugin download resolution.
/// </summary>
internal sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string? TagName { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("zipball_url")]
    public string? ZipballUrl { get; set; }

    [JsonPropertyName("assets")]
    public List<GitHubReleaseAsset>? Assets { get; set; }
}
