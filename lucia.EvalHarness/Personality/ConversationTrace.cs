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
    /// Formats the trace for judge input, wrapped in evaluation framing
    /// to avoid content filter triggers from personality instructions.
    /// </summary>
    public string Format()
    {
        return $"""
            The following is a transcript from a text-rewriting quality test. A model was given a personality description and asked to rephrase a factual smart home response. Please evaluate the quality of the rewrite.

            [Personality Description Given to Model]
            {SystemPrompt}

            [Original Factual Response]
            {OriginalResponse}

            [Rephrase Instruction Given to Model]
            {UserMessage}

            [Model Output]
            {AssistantResponse}
            """;
    }
}
