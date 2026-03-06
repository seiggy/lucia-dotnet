using lucia.Agents.Models;

namespace lucia.Agents.Abstractions;

/// <summary>
/// Implemented by skills whose entity-matching parameters (threshold, weight,
/// drop-off ratio) can be optimized at runtime via the skill optimizer UI.
/// </summary>
public interface IOptimizableSkill
{
    /// <summary>
    /// Human-readable skill name shown in the optimizer UI (e.g. "Light Control").
    /// </summary>
    string SkillDisplayName { get; }

    /// <summary>
    /// Unique skill identifier used in API routes (e.g. "light-control").
    /// </summary>
    string SkillId { get; }

    /// <summary>
    /// The agent ID that owns this skill (e.g. "light-agent", "climate-agent").
    /// Used to filter traces when importing search terms from trace history.
    /// </summary>
    string AgentId { get; }

    /// <summary>
    /// Tool method names whose arguments contain search terms for entity matching.
    /// Used by the trace import feature to extract search terms from tool call history.
    /// </summary>
    IReadOnlyList<string> SearchToolNames { get; }

    /// <summary>
    /// Configuration section name in MongoDB for this skill's options
    /// (e.g. "LightControlSkill"). Used to save optimized values.
    /// </summary>
    string ConfigSectionName { get; }

    /// <summary>
    /// The Home Assistant entity domain(s) this skill manages (e.g. "light").
    /// Used to query <see cref="IEntityLocationService"/> for the device list
    /// shown in the optimizer UI. Shared with the Entity Locations page.
    /// </summary>
    IReadOnlyList<string> EntityDomains { get; }

    /// <summary>
    /// Returns the current hybrid match options from the skill's configuration.
    /// Used as the starting point for optimization.
    /// </summary>
    HybridMatchOptions GetCurrentMatchOptions();
}
