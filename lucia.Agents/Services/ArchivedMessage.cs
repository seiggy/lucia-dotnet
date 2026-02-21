using System.Text.Json.Serialization;

namespace lucia.Agents.Services;

/// <summary>
/// A single message in an archived task's conversation history.
/// </summary>
public sealed class ArchivedMessage
{
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    [JsonPropertyName("text")]
    public string? Text { get; init; }

    [JsonPropertyName("messageId")]
    public string? MessageId { get; init; }
}
