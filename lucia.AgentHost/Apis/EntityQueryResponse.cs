namespace lucia.AgentHost.Apis;

/// <summary>
/// Represents a paginated entity query response.
/// </summary>
public sealed class EntityQueryResponse
{
    /// <summary>
    /// Gets the entities for the requested page.
    /// </summary>
    public IReadOnlyList<EntityQueryItem> Items { get; init; } = [];

    /// <summary>
    /// Gets the total number of entities matching the current filters.
    /// </summary>
    public int TotalCount { get; init; }

    /// <summary>
    /// Gets the current page number.
    /// </summary>
    public int Page { get; init; }

    /// <summary>
    /// Gets the current page size.
    /// </summary>
    public int PageSize { get; init; }
}
