

namespace lucia.Agents.CommandTracing;

/// <summary>
/// Result of the command pattern matching phase.
/// </summary>
public sealed record CommandTraceMatch
{
    public required bool IsMatch { get; init; }
    public required float Confidence { get; init; }
    public string? PatternId { get; init; }
    public string? SkillId { get; init; }
    public string? Action { get; init; }
    public string? TemplateUsed { get; init; }
    public IReadOnlyDictionary<string, string>? CapturedValues { get; init; }
    public required double MatchDurationMs { get; init; }

    /// <summary>
    /// Character-position highlights for overlay rendering on the user's input text.
    /// </summary>
    public IReadOnlyList<TokenHighlight> TokenHighlights { get; init; } = [];
}
