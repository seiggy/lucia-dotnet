using lucia.Agents.Models;
using lucia.Agents.Models.HomeAssistant;

namespace lucia.Agents.Abstractions;

public interface IAgentFilteredEntityLocationService
{
    /// <summary>
    /// Finds visible entities in matching locations using a trusted runtime agent ID.
    /// Null allow-list means unrestricted, empty means hidden, otherwise the caller must be listed.
    /// Implementations without this capability fail closed rather than using unscoped inventory.
    /// </summary>
    Task<IReadOnlyList<HomeAssistantEntity>> FindEntitiesByLocationForAgentAsync(
        string locationName,
        string callerAgentId,
        IReadOnlyList<string>? domainFilter = null,
        HybridMatchOptions? options = null,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Agent-scoped location lookup is not supported by this implementation.");

    /// <summary>Searches visible entity candidates only, before fuzzy or embedding scoring.</summary>
    Task<IReadOnlyList<EntityMatchResult<HomeAssistantEntity>>> SearchEntitiesForAgentAsync(
        string query,
        string callerAgentId,
        IReadOnlyList<string>? domainFilter = null,
        HybridMatchOptions? options = null,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Agent-scoped entity search is not supported by this implementation.");

    Task<HierarchicalSearchResult> SearchHierarchyForAgentAsync(
        string query,
        HybridMatchOptions? options,
        IReadOnlyList<string>? domainFilter,
        string callerAgentId,
        CancellationToken ct = default);
}
