namespace lucia.Agents.Orchestration.Models;

/// <summary>
/// Result from the orchestration pipeline, carrying the response text
/// and whether the conversation requires further user input.
/// </summary>
public sealed record OrchestratorResult
{
    /// <summary>The response text to return to the user (may be a clarifying question).</summary>
    public required string Text { get; init; }

    /// <summary>
    /// When true, the response is a clarifying question and the conversation
    /// should remain open for the user's follow-up.
    /// </summary>
    public bool NeedsInput { get; init; }
}
