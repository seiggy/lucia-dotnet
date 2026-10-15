using System.Collections.Immutable;
using lucia.Agents.Integration;
using lucia.Agents.Models;
using lucia.Agents.Models.HomeAssistant;
using lucia.HomeAssistant.Models;
using lucia.HomeAssistant.Services;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.Services;

/// <summary>
/// Reads floors, areas and entities from the Home Assistant registries and builds a
/// <see cref="LocationSnapshot"/> with phonetic keys. Embeddings are generated separately.
/// </summary>
public sealed class LocationSnapshotLoader
{
    private readonly IHomeAssistantClient _haClient;
    private readonly ILogger<LocationSnapshotLoader> _logger;

    public LocationSnapshotLoader(IHomeAssistantClient haClient, ILogger<LocationSnapshotLoader> logger)
    {
        _haClient = haClient;
        _logger = logger;
    }

    public async Task<LocationSnapshot> LoadAsync(bool exposedEntitiesOnly, CancellationToken ct = default)
    {
        // Fetch all three registries in parallel
        var floorTask = _haClient.GetFloorRegistryAsync(ct);
        var areaTask = _haClient.GetAreaRegistryAsync(ct);
        var entityTask = _haClient.GetEntityRegistryAsync(ct);

        await Task.WhenAll(floorTask, areaTask, entityTask).ConfigureAwait(false);

        var floorEntries = await floorTask.ConfigureAwait(false);
        var areaEntries = await areaTask.ConfigureAwait(false);
        var entityEntries = await entityTask.ConfigureAwait(false);

        if (exposedEntitiesOnly)
        {
            entityEntries = await FilterToExposedAsync(entityEntries, ct).ConfigureAwait(false);
        }

        // Get authoritative area→entity mapping via Jinja (handles device inheritance)
        var areaEntityMap = await LoadAreaEntityMapAsync(ct).ConfigureAwait(false);

        var floors = BuildFloors(floorEntries);
        var areas = BuildAreas(areaEntries, areaEntityMap);
        var entities = BuildEntities(entityEntries, areaEntityMap);
        GeneratePhoneticData(floors, areas, entities);

        return LocationSnapshot.Create(floors, areas, entities);
    }

    // A failure here propagates. Falling back to the full registry would hand agents entities the user
    // chose not to expose, so the caller keeps the previous snapshot and retries instead.
    private async Task<EntityRegistryEntry[]> FilterToExposedAsync(EntityRegistryEntry[] entityEntries, CancellationToken ct)
    {
        var exposed = await _haClient.GetExposedEntitiesAsync(ct).ConfigureAwait(false);
        var exposedIds = new HashSet<string>(
            exposed.Where(kv => kv.Value.IsExposedToAny).Select(kv => kv.Key),
            StringComparer.OrdinalIgnoreCase);

        var filtered = entityEntries.Where(e => exposedIds.Contains(e.EntityId)).ToArray();
        _logger.LogInformation(
            "Filtered to exposed entities only: {Before} → {After}",
            entityEntries.Length, filtered.Length);
        return filtered;
    }

    private async Task<Dictionary<string, List<string>>> LoadAreaEntityMapAsync(CancellationToken ct)
    {
        // Jinja template returns JSON: [{"area":"Kitchen","entities":["light.kitchen",...]},...]
        const string template =
            "[{% for id in areas() %}{% if not loop.first %}, {% endif %}" +
            "{\"area_id\":\"{{ id }}\",\"area_name\":\"{{ area_name(id) }}\",\"entities\":" +
            "[{% for e in area_entities(id) %}{% if not loop.first %}, {% endif %}\"{{ e }}\"{% endfor %}]}" +
            "{% endfor %}]";

        try
        {
            var result = await _haClient.RunTemplateAsync<List<JinjaAreaResult>>(template, ct).ConfigureAwait(false);
            var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in result)
            {
                map[entry.AreaId] = entry.Entities;
            }
            return map;
        }
        catch (Exception ex) when (!ex.IsCancellation())
        {
            _logger.LogWarning(ex, "Failed to load area→entity map via Jinja template, falling back to registry area_id only");
            return new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static ImmutableArray<FloorInfo> BuildFloors(FloorRegistryEntry[] entries)
    {
        return [
            ..entries.Select(f => new FloorInfo
            {
                FloorId = f.FloorId,
                Aliases = EntityMatchNameFormatter.SanitizeAliases(f.Aliases).AsReadOnly(),
                Name = EntityMatchNameFormatter.ResolveName(
                    f.Name,
                    f.Aliases,
                    f.FloorId),
                Level = f.Level,
                Icon = f.Icon
            })
        ];
    }

    private static ImmutableArray<AreaInfo> BuildAreas(
        AreaRegistryEntry[] entries,
        Dictionary<string, List<string>> areaEntityMap)
    {
        return [
            ..entries.Select(a => new AreaInfo
            {
                AreaId = a.AreaId,
                Name = EntityMatchNameFormatter.ResolveName(
                    a.Name,
                    a.Aliases,
                    a.AreaId),
                FloorId = a.FloorId,
                Aliases = EntityMatchNameFormatter.SanitizeAliases(a.Aliases).AsReadOnly(),
                EntityIds = areaEntityMap.TryGetValue(a.AreaId, out var entityIds)
                    ? entityIds.AsReadOnly()
                    : [],
                Icon = a.Icon,
                Labels = a.Labels.AsReadOnly()
            })
        ];
    }

    private static ImmutableArray<HomeAssistantEntity> BuildEntities(
        EntityRegistryEntry[] entries,
        Dictionary<string, List<string>> areaEntityMap)
    {
        // Build reverse lookup: entity_id → area_id from Jinja (authoritative)
        var entityToArea = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (areaId, entityIds) in areaEntityMap)
        {
            foreach (var entityId in entityIds)
            {
                entityToArea.TryAdd(entityId, areaId);
            }
        }

        return [
            ..entries
                .Where(e => e.DisabledBy is null) // Exclude disabled entities
                .Select(e => new HomeAssistantEntity
                {
                    EntityId = e.EntityId,
                    FriendlyName = EntityMatchNameFormatter.ResolveName(
                        e.Name ?? e.OriginalName,
                        e.Aliases,
                        e.EntityId,
                        stripDomainFromId: true),
                    Aliases = EntityMatchNameFormatter.SanitizeAliases(e.Aliases).AsReadOnly(),
                    // Prefer Jinja area (device-inherited), fall back to registry direct assignment
                    AreaId = entityToArea.TryGetValue(e.EntityId, out var jinjaArea)
                        ? jinjaArea
                        : e.AreaId,
                    Platform = e.Platform,
                    SupportedFeatures = (SupportedFeaturesFlags)e.SupportedFeatures
                })
        ];
    }

    private static void GeneratePhoneticData(
        ImmutableArray<FloorInfo> floors,
        ImmutableArray<AreaInfo> areas,
        ImmutableArray<HomeAssistantEntity> entities)
    {
        foreach (var floor in floors)
        {
            floor.PhoneticKeys = StringSimilarity.BuildPhoneticKeys(floor.Name);
            floor.AliasPhoneticKeys = BuildAliasPhoneticKeys(floor.Aliases);
        }

        foreach (var area in areas)
        {
            area.PhoneticKeys = StringSimilarity.BuildPhoneticKeys(area.Name);
            area.AliasPhoneticKeys = BuildAliasPhoneticKeys(area.Aliases);
        }

        foreach (var entity in entities)
        {
            entity.PhoneticKeys = StringSimilarity.BuildPhoneticKeys(entity.FriendlyName);
            entity.AliasPhoneticKeys = BuildAliasPhoneticKeys(entity.Aliases);
        }
    }

    private static IReadOnlyList<string[]> BuildAliasPhoneticKeys(IReadOnlyList<string> aliases)
    {
        if (aliases.Count == 0)
            return [];

        var result = new string[aliases.Count][];
        for (var i = 0; i < aliases.Count; i++)
            result[i] = StringSimilarity.BuildPhoneticKeys(aliases[i]);
        return result;
    }

    private sealed class JinjaAreaResult
    {
        [System.Text.Json.Serialization.JsonPropertyName("area_id")]
        public string AreaId { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("area_name")]
        public string AreaName { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("entities")]
        public List<string> Entities { get; set; } = [];
    }
}
