namespace lucia.Agents.GitHubCopilot.Models;

/// <summary>
/// DTO returned from the Copilot CLI connect + list-models flow.
/// </summary>
public sealed record CopilotModelInfo(
    string Id,
    string Name,
    bool SupportsVision,
    bool SupportsReasoningEffort,
    double? MaxPromptTokens,
    double? MaxOutputTokens,
    double MaxContextWindowTokens,
    string? PolicyState,
    string? PolicyTerms,
    double BillingMultiplier,
    List<string> SupportedReasoningEfforts,
    string? DefaultReasoningEffort);