using lucia.Agents.Models;
using lucia.Agents.Models.HomeAssistant;

namespace lucia.Tests.TestDoubles;

/// <summary>
/// Builds <see cref="HierarchicalSearchResult"/> values for faked hierarchy searches.
/// </summary>
internal static class HierarchyResults
{
    public static HierarchicalSearchResult Resolved(IReadOnlyList<HomeAssistantEntity> entities) => new()
    {
        FloorMatches = [],
        AreaMatches = [],
        EntityMatches = [],
        ResolvedEntities = entities,
        ResolutionStrategy = entities.Count > 0 ? ResolutionStrategy.Area : ResolutionStrategy.None,
        ResolutionReason = "Test hierarchy result"
    };
}
