using System.Text.Json.Serialization;

namespace lucia.HomeAssistant.Models;

/// <summary>
/// Result from HA media browser — represents a media directory or a playable media item.
/// Returned by media_player.browse_media service or WebSocket media_source/browse_media command.
/// </summary>
public sealed class MediaBrowseResult
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("media_class")]
    public string MediaClass { get; set; } = string.Empty;

    [JsonPropertyName("media_content_type")]
    public string? MediaContentType { get; set; }

    [JsonPropertyName("media_content_id")]
    public string? MediaContentId { get; set; }

    [JsonPropertyName("can_play")]
    public bool CanPlay { get; set; }

    [JsonPropertyName("can_expand")]
    public bool CanExpand { get; set; }

    [JsonPropertyName("thumbnail")]
    public string? Thumbnail { get; set; }

    [JsonPropertyName("children")]
    public List<MediaBrowseResult>? Children { get; set; }

    [JsonPropertyName("children_media_class")]
    public string? ChildrenMediaClass { get; set; }

    [JsonPropertyName("not_shown")]
    public int NotShown { get; set; }
}
