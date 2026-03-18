using System.Text.Json.Serialization;

namespace lucia.AgentHost.Conversation.Models;

/// <summary>
/// Structured request from Home Assistant's conversation pipeline.
/// Separates user speech text from device/session context for optimized command parsing.
/// </summary>
public sealed record ConversationRequest
{
    [JsonPropertyName("text")]
    public required string Text { get; init; }

    [JsonPropertyName("context")]
    public required ConversationContext Context { get; init; }

    /// <summary>
    /// Optional system prompt override. When set, replaces the default prompt template
    /// used for LLM fallback requests.
    /// </summary>
    [JsonPropertyName("promptOverride")]
    public string? PromptOverride { get; init; }
}
