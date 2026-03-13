namespace lucia.Agents.Abstractions;

public sealed record CommandPatternDefinition
{
    public required string Id { get; init; }

    public required string SkillId { get; init; }

    public required string Action { get; init; }

    public required string[] Templates { get; init; }

    public float MinConfidence { get; init; } = 0.8f;

    public int Priority { get; init; }
}
