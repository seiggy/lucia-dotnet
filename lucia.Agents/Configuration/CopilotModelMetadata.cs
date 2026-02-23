namespace lucia.Agents.Configuration;

/// <summary>
/// Stores model details fetched from the GitHub Copilot CLI via ListModelsAsync.
/// Persisted alongside the ModelProvider so the correct model config is available at session creation time.
/// </summary>
public sealed class CopilotModelMetadata
{
    /// <summary>
    /// Whether the model supports vision/image inputs.
    /// </summary>
    public bool SupportsVision { get; set; }

    /// <summary>
    /// Whether the model supports reasoning effort configuration.
    /// </summary>
    public bool SupportsReasoningEffort { get; set; }

    /// <summary>
    /// Maximum prompt tokens, if reported by the model.
    /// </summary>
    public double? MaxPromptTokens { get; set; }

    /// <summary>
    /// Maximum output tokens, if reported by the model.
    /// </summary>
    public double? MaxOutputTokens { get; set; }

    /// <summary>
    /// Maximum context window size in tokens.
    /// </summary>
    public double MaxContextWindowTokens { get; set; }

    /// <summary>
    /// Policy state (e.g., "enabled", "disabled").
    /// </summary>
    public string? PolicyState { get; set; }

    /// <summary>
    /// Policy terms URL, if any.
    /// </summary>
    public string? PolicyTerms { get; set; }

    /// <summary>
    /// Billing multiplier for the model.
    /// </summary>
    public double BillingMultiplier { get; set; }

    /// <summary>
    /// Reasoning efforts supported by the model (e.g., "low", "medium", "high").
    /// </summary>
    public List<string> SupportedReasoningEfforts { get; set; } = [];

    /// <summary>
    /// Default reasoning effort for the model.
    /// </summary>
    public string? DefaultReasoningEffort { get; set; }
}
