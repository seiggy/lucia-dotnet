using System.Text.Json.Serialization;

namespace lucia.Agents.Services;

/// <summary>
/// Minimal DTO for a GitHub Release asset from the REST API.
/// </summary>
internal sealed class GitHubReleaseAsset
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = string.Empty;

    [JsonPropertyName("content_type")]
    public string? ContentType { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }
}
