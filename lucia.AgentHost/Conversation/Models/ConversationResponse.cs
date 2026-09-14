using System.Text.Json.Serialization;

namespace lucia.AgentHost.Conversation.Models;

/// <summary>
/// Response from the /api/conversation endpoint.
/// Type distinguishes local commands, voice onboarding, errors, and LLM responses.
/// </summary>
public sealed record ConversationResponse
{
    /// <summary>
    /// "command", "onboarding", "error", or "llm".
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

    /// <summary>
    /// When true, the response is a clarifying question and the conversation
    /// should remain open for the user's follow-up.
    /// </summary>
    [JsonPropertyName("needsInput")]
    public bool NeedsInput { get; init; }

    internal string? OnboardingStage { get; init; }

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
        string? conversationId,
        bool needsInput = false) => new()
    {
        Type = "llm",
        Text = text,
        ConversationId = conversationId,
        NeedsInput = needsInput,
    };
}
