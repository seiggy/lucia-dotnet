using System.Collections.Immutable;
using lucia.Agents.Models.HomeAssistant;

namespace lucia.Agents.Models;

/// <summary>
/// Immutable snapshot of all location data. Swapped atomically as a single reference
/// so all reader threads see a consistent view without locks.
/// </summary>
public sealed class LocationSnapshot
{
    public static readonly LocationSnapshot Empty = new(
        ImmutableArray<FloorInfo>.Empty,
        ImmutableArray<AreaInfo>.Empty,
        ImmutableArray<HomeAssistantEntity>.Empty,
        ImmutableDictionary<string, FloorInfo>.Empty,
        ImmutableDictionary<string, AreaInfo>.Empty,
        ImmutableDictionary<string, HomeAssistantEntity>.Empty);

    public LocationSnapshot(
        ImmutableArray<FloorInfo> floors,
        ImmutableArray<AreaInfo> areas,
        ImmutableArray<HomeAssistantEntity> entities,
        ImmutableDictionary<string, FloorInfo> floorById,
        ImmutableDictionary<string, AreaInfo> areaById,
        ImmutableDictionary<string, HomeAssistantEntity> entityById)
    {
        Floors = floors;
        Areas = areas;
        Entities = entities;
        FloorById = floorById;
        AreaById = areaById;
        EntityById = entityById;
    }

    public ImmutableArray<FloorInfo> Floors { get; }
    public ImmutableArray<AreaInfo> Areas { get; }
    public ImmutableArray<HomeAssistantEntity> Entities { get; }
    public ImmutableDictionary<string, FloorInfo> FloorById { get; }
    public ImmutableDictionary<string, AreaInfo> AreaById { get; }
    public ImmutableDictionary<string, HomeAssistantEntity> EntityById { get; }
}
