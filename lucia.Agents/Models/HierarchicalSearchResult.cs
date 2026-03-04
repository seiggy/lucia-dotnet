using lucia.Agents.Abstractions;
using lucia.Agents.Models.HomeAssistant;

namespace lucia.Agents.Models;

/// <summary>
/// Result of a hierarchical search that walks floors → areas → entities.
/// Contains scored matches at each level plus the entities that were
/// resolved by walking the hierarchy downward from matched locations.
/// </summary>
public sealed record HierarchicalSearchResult
{
    /// <summary>
    /// Floors that matched the query directly, with their hybrid scores.
    /// </summary>
    public required IReadOnlyList<EntityMatchResult<FloorInfo>> FloorMatches { get; init; }

    /// <summary>
    /// Areas that matched the query directly, with their hybrid scores.
    /// </summary>
    public required IReadOnlyList<EntityMatchResult<AreaInfo>> AreaMatches { get; init; }

    /// <summary>
    /// Entities that matched the query directly, with their hybrid scores.
    /// </summary>
    public required IReadOnlyList<EntityMatchResult<HomeAssistantEntity>> EntityMatches { get; init; }

    /// <summary>
    /// All entities resolved by walking the hierarchy downward from matched
    /// floors and areas. These entities may not have matched the query directly
    /// but belong to a matched location.
    /// </summary>
    public required IReadOnlyList<HomeAssistantEntity> ResolvedEntities { get; init; }
    
    /// <summary>
    /// Which resolution path was chosen after comparing match quality
    /// across all levels.
    /// </summary>
    public required ResolutionStrategy ResolutionStrategy { get; init; }

    /// <summary>
    /// Human-readable explanation of why this strategy was chosen.
    /// Useful for debugging (e.g. "Entity path: best entity hybrid score 0.89
    /// exceeds best area hybrid score 0.33 by 0.56, above margin 0.30").
    /// </summary>
    public required string ResolutionReason { get; init; }

    /// <summary>
    /// The best hybrid score similarity among direct entity matches, or null
    /// if no entities matched.
    /// </summary>
    public double? BestEntityScore { get; init; }

    /// <summary>
    /// The best hybrid score similarity among direct area matches, or null
    /// if no areas matched.
    /// </summary>
    public double? BestAreaScore { get; init; }

    /// <summary>
    /// The best hybrid score similarity among direct floor matches, or null
    /// if no floors matched.
    /// </summary>
    public double? BestFloorScore { get; init; }
}
