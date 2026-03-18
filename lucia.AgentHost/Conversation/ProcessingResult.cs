using lucia.AgentHost.Conversation.Models;

namespace lucia.AgentHost.Conversation;

/// <summary>
/// Result from <see cref="ConversationCommandProcessor"/> indicating how to respond.
/// The API layer uses <see cref="Kind"/> to decide between instant JSON and SSE streaming.
/// </summary>
public sealed record ProcessingResult
{
    public required ProcessingKind Kind { get; init; }

    /// <summary>
    /// The fully formed response. Present for <see cref="ProcessingKind.CommandHandled"/>
    /// and <see cref="ProcessingKind.LlmCompleted"/>.
    /// </summary>
    public ConversationResponse? Response { get; init; }

    /// <summary>
    /// The reconstructed prompt for LLM streaming. Present for <see cref="ProcessingKind.LlmFallback"/>
    /// when the engine is not available synchronously and the API must stream.
    /// </summary>
    public string? LlmPrompt { get; init; }

    /// <summary>Conversation ID for session continuity.</summary>
    public string? ConversationId { get; init; }

    /// <summary>Command was parsed and executed; return instant JSON.</summary>
    public static ProcessingResult CommandHandled(ConversationResponse response) => new()
    {
        Kind = ProcessingKind.CommandHandled,
        Response = response,
        ConversationId = response.ConversationId,
    };

    /// <summary>LLM completed synchronously; return JSON response.</summary>
    public static ProcessingResult LlmCompleted(ConversationResponse response) => new()
    {
        Kind = ProcessingKind.LlmCompleted,
        Response = response,
        ConversationId = response.ConversationId,
    };

    /// <summary>LLM fallback needed but engine unavailable; API should stream.</summary>
    public static ProcessingResult LlmFallback(string? conversationId, string prompt) => new()
    {
        Kind = ProcessingKind.LlmFallback,
        ConversationId = conversationId,
        LlmPrompt = prompt,
    };
}

/// <summary>Discriminator for how the API layer should respond.</summary>
public enum ProcessingKind
{
    /// <summary>Command matched and executed — instant JSON response.</summary>
    CommandHandled,

    /// <summary>LLM completed synchronously — JSON response.</summary>
    LlmCompleted,

    /// <summary>LLM fallback pending — API should handle streaming.</summary>
    LlmFallback,
}
