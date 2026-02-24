namespace lucia.Agents.Models;

/// <summary>
/// Cached floor metadata from Home Assistant.
/// Stored separately in Redis as lucia:location:floors.
/// </summary>
public sealed class FloorInfo
{
    public required string FloorId { get; init; }
    public required string Name { get; init; }
    public IReadOnlyList<string> Aliases { get; init; } = [];
    public int? Level { get; init; }
    public string? Icon { get; init; }
}
