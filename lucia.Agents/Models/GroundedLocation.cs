using lucia.Agents.Models.HomeAssistant;

namespace lucia.Agents.Models;

/// <summary>
/// Result of location grounding: either a single area or all areas on a floor.
/// </summary>
internal sealed record GroundedLocation(IReadOnlyList<AreaInfo> Areas, FloorInfo? Floor = null)
{
    public AreaInfo? SingleArea => Areas.Count == 1 ? Areas[0] : null;
    public bool IsFloorLevel => Floor is not null;

    public string DisplayName => Floor is not null
        ? Floor.Name
        : SingleArea?.Name ?? "(unknown)";
}
