namespace lucia.EvalHarness.Optimization;

/// <summary>
/// A single prompt improvement suggestion with reasoning and predicted impact.
/// </summary>
public sealed class PromptSuggestion
{
    /// <summary>
    /// Type of change: "add_example", "clarify_instruction", "add_constraint",
    /// "restructure", "remove_ambiguity", "add_tool_guidance".
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Where in the prompt this change should be applied.
    /// </summary>
    public required string Location { get; init; }

    /// <summary>
    /// The suggested content to add or change.
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// Why this change would help, based on failure analysis.
    /// </summary>
    public required string Reasoning { get; init; }

    /// <summary>
    /// Predicted score improvement (e.g., "+15 ToolSelection").
    /// </summary>
    public string? PredictedImpact { get; init; }
}
