namespace lucia.Wyoming.Models;

/// <summary>
/// Event raised when the active STT model changes.
/// </summary>
public sealed record ActiveModelChangedEvent
{
    public required string ModelId { get; init; }

    public required string ModelPath { get; init; }
}
