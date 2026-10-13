namespace lucia.Agents.Abstractions;

public sealed record CommandPatternDefinition
{
    public required string Id { get; init; }

    public required string SkillId { get; init; }

    public required string Action { get; init; }

    public required string[] Templates { get; init; }

    public float MinConfidence { get; init; } = 0.8f;

    public int Priority { get; init; }

    /// <summary>
    /// Marks patterns that answer status questions ("is the front door locked").
    /// Query patterns only match transcripts classified as status questions; command
    /// patterns never do, and vice versa.
    /// </summary>
    public bool IsQuery { get; init; }
}
