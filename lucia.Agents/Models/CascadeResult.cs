namespace lucia.Agents.Models;

public sealed record CascadeResult
{
    public required bool IsResolved { get; init; }
    public string? ResolvedArea { get; init; }
    public IReadOnlyList<string> ResolvedEntityIds { get; init; } = [];
    public BailReason? BailReason { get; init; }
    public string? Explanation { get; init; }
}
