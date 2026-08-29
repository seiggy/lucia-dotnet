using lucia.Agents.Models;

namespace lucia.Agents.Abstractions;

public interface IAgentFilteredEntityLocationService
{
    Task<HierarchicalSearchResult> SearchHierarchyForAgentAsync(
        string query,
        HybridMatchOptions? options,
        IReadOnlyList<string>? domainFilter,
        string callerAgentId,
        CancellationToken ct = default);
}
