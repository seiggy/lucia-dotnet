using lucia.Agents.Abstractions;
using Microsoft.Extensions.AI;

namespace lucia.Agents.Models.HomeAssistant;

/// <summary>
/// Cached area metadata from Home Assistant.
/// Stored separately in Redis as lucia:location:areas.
/// Has a 0..1 relationship with <see cref="FloorInfo"/> via <see cref="FloorId"/>.
/// </summary>
public sealed class AreaInfo : IMatchableEntity
{
    public required string AreaId { get; init; }
    public required string Name { get; init; }
    public string? FloorId { get; init; }
    public IReadOnlyList<string> Aliases { get; init; } = [];
    public IReadOnlyList<string> EntityIds { get; init; } = [];
    public string? Icon { get; init; }
    public IReadOnlyList<string> Labels { get; init; } = [];
    
    public Embedding<float>? NameEmbedding { get; set; }

    public string MatchableName => Name;
    Embedding<float>? IMatchableEntity.NameEmbedding => NameEmbedding;
    public string[] PhoneticKeys { get; set; } = [];
    public IReadOnlyList<string[]> AliasPhoneticKeys { get; set; } = [];
    IReadOnlyList<string> IMatchableEntity.MatchableAliases => Aliases;
}
