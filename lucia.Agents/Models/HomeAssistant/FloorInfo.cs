using lucia.Agents.Abstractions;
using Microsoft.Extensions.AI;

namespace lucia.Agents.Models.HomeAssistant;

/// <summary>
/// Cached floor metadata from Home Assistant.
/// Stored separately in Redis as lucia:location:floors.
/// </summary>
public sealed class FloorInfo : IMatchableEntity
{
    public required string FloorId { get; init; }
    public required string Name { get; init; }
    public IReadOnlyList<string> Aliases { get; init; } = [];
    public int? Level { get; init; }
    public string? Icon { get; init; }

    // ── IMatchableEntity ────────────────────────────────────────

    public Embedding<float>? NameEmbedding { get; set; }
    public string[] PhoneticKeys { get; set; } = [];
    public IReadOnlyList<string[]> AliasPhoneticKeys { get; set; } = [];
    string IMatchableEntity.MatchableName => Name;
    IReadOnlyList<string> IMatchableEntity.MatchableAliases => Aliases;
}
