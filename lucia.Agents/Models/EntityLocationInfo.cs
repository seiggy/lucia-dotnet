using lucia.HomeAssistant.Models;

namespace lucia.Agents.Models;

/// <summary>
/// Cached entity location metadata from Home Assistant.
/// Stored separately in Redis as lucia:location:entities.
/// Has a 0..1 relationship with <see cref="AreaInfo"/> via <see cref="AreaId"/>.
/// Contains no entity state — only location and identity metadata.
/// </summary>
public sealed class EntityLocationInfo
{
    public required string EntityId { get; init; }
    public required string FriendlyName { get; init; }
    public string Domain => EntityId.Split('.')[0];
    public IReadOnlyList<string> Aliases { get; init; } = [];
    public string? AreaId { get; init; }
    public string? Platform { get; init; }
    public SupportedFeaturesFlags SupportedFeatures { get; init; }
}
