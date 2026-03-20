namespace lucia.Agents.CommandTracing;

/// <summary>
/// Character-position highlight for a single token matched from the user's input text.
/// Used by the dashboard to render color-coded overlays.
/// </summary>
public sealed record TokenHighlight
{
    /// <summary>Start character index in the clean (speaker-tag-stripped) text.</summary>
    public required int Start { get; init; }

    /// <summary>End character index (exclusive) in the clean text.</summary>
    public required int End { get; init; }

    /// <summary>The type of template token this span matched.</summary>
    public required TokenHighlightType Type { get; init; }

    /// <summary>The capture key or literal text that was matched.</summary>
    public required string Value { get; init; }
}
