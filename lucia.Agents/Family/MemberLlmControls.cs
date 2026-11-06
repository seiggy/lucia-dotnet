namespace lucia.Agents.Family;

/// <summary>
/// Per-member model selection, spending limits, and content settings.
/// </summary>
public sealed class MemberLlmControls
{
    /// <summary>
    /// Model provider IDs this member may use. Empty allows every enabled provider.
    /// </summary>
    public List<string> AllowedProviderIds { get; init; } = [];

    /// <summary>
    /// Model provider used for this member instead of the agent's configured model, when allowed.
    /// </summary>
    public string? PreferredProviderId { get; init; }

    public decimal? DailyBudgetUsd { get; init; }

    public decimal? MonthlyBudgetUsd { get; init; }

    /// <summary>
    /// Extra system instructions, such as age-appropriate guidance.
    /// </summary>
    public string? SystemPrompt { get; init; }

    public List<string> BlockedTopics { get; init; } = [];

    public QuietHoursWindow? QuietHours { get; init; }
}
