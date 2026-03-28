namespace lucia.Agents.Models;

/// <summary>
/// Strategy for automatic entity-to-agent visibility assignment.
/// </summary>
public enum AutoAssignStrategy
{
    /// <summary>
    /// Set all entities to hidden from all agents (empty agent list).
    /// </summary>
    None,

    /// <summary>
    /// Use heuristic rules to match entities to agents based on domain,
    /// entity name patterns, and platform metadata.
    /// </summary>
    Smart
}
