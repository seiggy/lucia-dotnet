namespace lucia.Wyoming.Models;

public sealed record ActiveModelChangedEvent
{
    public required EngineType EngineType { get; init; }
    public required string ModelId { get; init; }
    public required string ModelPath { get; init; }
}
