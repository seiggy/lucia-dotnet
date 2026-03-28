namespace lucia.EvalHarness.Optimization;

/// <summary>
/// The result of an LLM-powered prompt optimization analysis.
/// Contains the original prompt, suggested changes, and the full revised prompt.
/// </summary>
public sealed class PromptOptimizationResult
{
    /// <summary>
    /// The agent that was analyzed.
    /// </summary>
    public required string AgentName { get; init; }

    /// <summary>
    /// The model the optimization targets.
    /// </summary>
    public required string TargetModel { get; init; }

    /// <summary>
    /// The original system prompt that was analyzed.
    /// </summary>
    public required string OriginalPrompt { get; init; }

    /// <summary>
    /// Current score of the target model on this agent.
    /// </summary>
    public required double CurrentScore { get; init; }

    /// <summary>
    /// The baseline score to target (from the best-performing model).
    /// </summary>
    public required double BaselineScore { get; init; }

    /// <summary>
    /// Individual optimization suggestions with reasoning.
    /// </summary>
    public required IReadOnlyList<PromptSuggestion> Suggestions { get; init; }

    /// <summary>
    /// The complete revised prompt incorporating all suggestions.
    /// </summary>
    public string? SuggestedPrompt { get; init; }

    /// <summary>
    /// Raw reasoning from the LLM about the analysis.
    /// </summary>
    public string? Analysis { get; init; }
}
