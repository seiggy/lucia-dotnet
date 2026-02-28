namespace lucia.Agents.Services;

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
    /// Returns the cached matchable entities for optimization.
    /// The optimizer searches these candidates with different parameter combinations.
    /// </summary>
    Task<IReadOnlyList<IMatchableEntity>> GetCachedEntitiesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the current hybrid match options from the skill's configuration.
    /// Used as the starting point for optimization.
    /// </summary>
    HybridMatchOptions GetCurrentMatchOptions();
}
