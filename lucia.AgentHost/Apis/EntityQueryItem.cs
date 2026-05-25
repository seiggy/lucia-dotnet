namespace lucia.AgentHost.Apis;

/// <summary>
/// Represents a single entity returned from the paginated entity query endpoint.
/// </summary>
public sealed class EntityQueryItem
{
    /// <summary>
    /// Gets the Home Assistant entity identifier.
    /// </summary>
    public required string EntityId { get; init; }

    /// <summary>
    /// Gets the display name shown to users.
    /// </summary>
    public required string FriendlyName { get; init; }

    /// <summary>
    /// Gets the entity domain.
    /// </summary>
    public required string Domain { get; init; }

    /// <summary>
    /// Gets alternate entity names.
    /// </summary>
    public IReadOnlyList<string> Aliases { get; init; } = [];

    /// <summary>
    /// Gets the Home Assistant area identifier.
    /// </summary>
    public string? AreaId { get; init; }

    /// <summary>
    /// Gets the resolved area name.
    /// </summary>
    public string? AreaName { get; init; }

    /// <summary>
    /// Gets the resolved floor name.
    /// </summary>
    public string? FloorName { get; init; }

    /// <summary>
    /// Gets the integration platform.
    /// </summary>
    public string? Platform { get; init; }

    /// <summary>
    /// Gets a value indicating whether an embedding exists for the entity.
    /// </summary>
    public bool EmbeddingGenerated { get; init; }

    /// <summary>
    /// Gets the agents allowed to access the entity.
    /// </summary>
    public IReadOnlyList<string>? IncludeForAgent { get; init; }
}
