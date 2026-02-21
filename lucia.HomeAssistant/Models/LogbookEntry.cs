using System.Text.Json.Serialization;

namespace lucia.HomeAssistant.Models;

/// <summary>
/// Logbook entry for entity state changes and events.
/// </summary>
public sealed record LogbookEntry
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("domain")]
    public string? Domain { get; init; }

    [JsonPropertyName("entity_id")]
    public string? EntityId { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("when")]
    public required string When { get; init; }

    [JsonPropertyName("context_user_id")]
    public string? ContextUserId { get; init; }
}
