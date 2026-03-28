namespace lucia.Agents.Models;

/// <summary>
/// Preview of an auto-assign operation showing what would be assigned without applying changes.
/// </summary>
public sealed class AutoAssignPreview
{
    public required AutoAssignStrategy Strategy { get; init; }
    public required int TotalEntities { get; init; }
    public required int AssignedCount { get; init; }
    public required int ExcludedCount { get; init; }
    public required IReadOnlyList<AutoAssignAgentGroup> AgentGroups { get; init; }
    public required IReadOnlyList<string> ExcludedSample { get; init; }

    /// <summary>
    /// The full entity-to-agent map that would be applied.
    /// Keys are entity IDs; values are agent name lists (empty list = no agents).
    /// </summary>
    public required IReadOnlyDictionary<string, List<string>?> EntityAgentMap { get; init; }
}
