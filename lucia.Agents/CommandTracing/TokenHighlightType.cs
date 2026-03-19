namespace lucia.Agents.CommandTracing;

/// <summary>
/// Classification of a token highlight span for dashboard rendering.
/// </summary>
public enum TokenHighlightType
{
    /// <summary>A required literal word from the template (e.g., "turn").</summary>
    Literal,

    /// <summary>A free-form captured value (e.g., {entity}).</summary>
    Capture,

    /// <summary>A constrained captured value (e.g., {action:on|off}).</summary>
    ConstrainedCapture,

    /// <summary>An optional literal word from the template (e.g., [the]).</summary>
    Optional,
}
