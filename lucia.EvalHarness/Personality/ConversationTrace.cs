namespace lucia.EvalHarness.Personality;

/// <summary>
/// Captures the full conversation trace of a personality rewrite for judge evaluation.
/// Format: System → User → Assistant → Personality output.
/// </summary>
public sealed class ConversationTrace
{
    /// <summary>
    /// The personality system prompt sent to the model-under-test.
    /// </summary>
    public required string SystemPrompt { get; init; }

    /// <summary>
    /// The rephrase instruction + original agent response sent as the user message.
    /// </summary>
    public required string UserMessage { get; init; }

    /// <summary>
    /// The rewritten response from the model-under-test.
    /// </summary>
    public required string AssistantResponse { get; init; }

    /// <summary>
    /// The original agent response before personality rewrite.
    /// </summary>
    public required string OriginalResponse { get; init; }

    /// <summary>
    /// Formats the trace for display or judge input.
    /// </summary>
    public string Format()
    {
        return $"""
            System: {SystemPrompt}
            ::
            User: {UserMessage}
            Assistant: {AssistantResponse}
            ::
            Personality: {AssistantResponse}
            """;
    }
}
