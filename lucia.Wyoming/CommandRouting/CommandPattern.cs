namespace lucia.Wyoming.CommandRouting;

public sealed record CommandPattern
{
    /// <summary>Pattern ID for telemetry/debugging (e.g., "light-on-off").</summary>
    public required string Id { get; init; }

    /// <summary>Target skill type name.</summary>
    public required string SkillId { get; init; }

    /// <summary>Action to invoke on the skill.</summary>
    public required string Action { get; init; }

    /// <summary>
    /// Template patterns with placeholders.
    /// Syntax: {name} = required capture, {name:opt1|opt2} = capture with allowed values, [word] = optional literal.
    /// E.g., "turn {action:on|off} [the] {entity}"
    /// </summary>
    public required IReadOnlyList<string> Templates { get; init; }

    /// <summary>Minimum confidence to fast-path (0.0-1.0).</summary>
    public float MinConfidence { get; init; } = 0.8f;

    /// <summary>Match priority (higher = preferred when multiple match).</summary>
    public int Priority { get; init; }
}
