namespace lucia.Agents.Models.HomeAssistant;

/// <summary>
/// Snapshot of embedding generation coverage and progress for the entity location cache.
/// </summary>
public sealed class EntityLocationEmbeddingProgress
{
    public required int FloorTotalCount { get; init; }
    public required int FloorGeneratedCount { get; init; }
    public required int AreaTotalCount { get; init; }
    public required int AreaGeneratedCount { get; init; }
    public required int EntityTotalCount { get; init; }
    public required int EntityGeneratedCount { get; init; }
    public required int EntityMissingCount { get; init; }
    public required bool IsGenerationRunning { get; init; }
    public DateTimeOffset? LastLoadedAt { get; init; }
}
