using System.Text.Json.Serialization;

namespace lucia.Wyoming.Models;

/// <summary>
/// DTO for the Hugging Face Hub <c>GET /api/models</c> response items.
/// Only the fields we need for catalog display and download are mapped.
/// </summary>
public sealed record HuggingFaceModelInfo
{
    [JsonPropertyName("_id")]
    public string? InternalId { get; init; }

    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("modelId")]
    public string ModelId { get; init; } = string.Empty;

    [JsonPropertyName("pipeline_tag")]
    public string? PipelineTag { get; init; }

    [JsonPropertyName("tags")]
    public string[] Tags { get; init; } = [];

    [JsonPropertyName("downloads")]
    public long Downloads { get; init; }

    [JsonPropertyName("likes")]
    public int Likes { get; init; }

    [JsonPropertyName("private")]
    public bool IsPrivate { get; init; }

    [JsonPropertyName("lastModified")]
    public DateTimeOffset? LastModified { get; init; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset? CreatedAt { get; init; }

    [JsonPropertyName("library_name")]
    public string? LibraryName { get; init; }
}
