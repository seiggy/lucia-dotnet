using System.Text.Json.Serialization;

namespace lucia.AgentHost.Conversation.Models;

/// <summary>
/// Response from the /api/conversation endpoint.
/// Type indicates whether the command was parsed locally or handled by the LLM.
/// </summary>
public sealed record ConversationResponse
{
    /// <summary>
    /// "command" for pattern-matched fast-path, "llm" for LLM fallback.
    /// </summary>
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("text")]
    public required string Text { get; init; }

    /// <summary>
    /// Present only when Type is "command". Contains details about the parsed command.
    /// </summary>
    [JsonPropertyName("command")]
    public CommandDetail? Command { get; init; }

    /// <summary>
    /// The conversation session identifier for multi-turn continuity.
    /// </summary>
    [JsonPropertyName("conversationId")]
    public string? ConversationId { get; init; }

    public static ConversationResponse FromCommand(
        string text,
        CommandDetail command,
        string? conversationId) => new()
    {
        Type = "command",
        Text = text,
        Command = command,
        ConversationId = conversationId,
    };

    public static ConversationResponse FromLlm(
        string text,
        string? conversationId) => new()
    {
        Type = "llm",
        Text = text,
        ConversationId = conversationId,
    };
}
