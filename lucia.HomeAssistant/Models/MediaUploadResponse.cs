using System.Text.Json.Serialization;

namespace lucia.HomeAssistant.Models;

/// <summary>
/// Response from HA media source upload endpoint.
/// POST /api/media_source/local_source/upload returns the content ID of the uploaded file.
/// </summary>
public sealed class MediaUploadResponse
{
    [JsonPropertyName("media_content_id")]
    public string MediaContentId { get; set; } = string.Empty;
}
