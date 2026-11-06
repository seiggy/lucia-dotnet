namespace lucia.Agents.Family;

/// <summary>
/// A per-member override of the role preset for one agent or tool.
/// </summary>
public sealed class CapabilityRule
{
    public required CapabilityKind Kind { get; init; }

    /// <summary>
    /// Agent name or tool/skill identifier, matched case-insensitively.
    /// </summary>
    public required string CapabilityId { get; init; }

    public required ActionRule Rule { get; init; }
}
