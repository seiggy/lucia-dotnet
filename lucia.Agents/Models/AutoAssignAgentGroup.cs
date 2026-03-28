namespace lucia.Agents.Models;

/// <summary>
/// A group of entities assigned to a specific agent in an auto-assign preview.
/// </summary>
public sealed class AutoAssignAgentGroup
{
    public required string AgentName { get; init; }
    public required int Count { get; init; }
    public required IReadOnlyList<string> EntityIds { get; init; }
}
