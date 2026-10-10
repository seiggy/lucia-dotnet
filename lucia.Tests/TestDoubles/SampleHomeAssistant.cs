using System.Text.Json;
using FakeItEasy;
using lucia.HomeAssistant.Models;
using lucia.HomeAssistant.Services;

namespace lucia.Tests.TestDoubles;

/// <summary>
/// A scripted Home Assistant with a small two-floor home: registries, the area-to-entity template
/// and the exposed-entity list. Tests change the public collections to simulate edits in Home Assistant.
/// </summary>
public sealed class SampleHomeAssistant
{
    private int _registryLoads;

    public SampleHomeAssistant()
    {
        Floors =
        [
            new FloorRegistryEntry { FloorId = "ground", Name = "Ground Floor", Level = 0, Aliases = ["downstairs"] },
            new FloorRegistryEntry { FloorId = "upper", Name = "Upper Floor", Level = 1, Aliases = ["upstairs"] },
        ];

        Areas =
        [
            new AreaRegistryEntry { AreaId = "kitchen", Name = "Kitchen", FloorId = "ground" },
            new AreaRegistryEntry { AreaId = "living_room", Name = "Living Room", FloorId = "ground", Aliases = ["lounge"] },
            new AreaRegistryEntry { AreaId = "bedroom", Name = "Bedroom", FloorId = "upper" },
            new AreaRegistryEntry { AreaId = "office", Name = "Office", FloorId = "upper" },
        ];

        Entities =
        [
            Entity("light.kitchen_ceiling", "Kitchen Ceiling"),
            Entity("light.kitchen_island", "Kitchen Island"),
            Entity("light.living_room_lamp", "Living Room Lamp", aliases: ["reading lamp"]),
            Entity("light.bedroom_main", "Bedroom Light"),
            Entity("climate.upstairs_thermostat", "Upstairs Thermostat"),
            Entity("media_player.office_speaker", "Office Speaker"),
            Entity("light.porch", "Porch Light"),
            Entity("light.retired", "Retired Light", disabledBy: "user"),
        ];

        AreaEntities = new Dictionary<string, List<string>>
        {
            ["kitchen"] = ["light.kitchen_ceiling", "light.kitchen_island"],
            ["living_room"] = ["light.living_room_lamp"],
            ["bedroom"] = ["light.bedroom_main", "climate.upstairs_thermostat"],
            ["office"] = ["media_player.office_speaker"],
        };

        ExposedEntityIds = ["light.kitchen_ceiling", "light.living_room_lamp"];

        Client = A.Fake<IHomeAssistantClient>();
        A.CallTo(() => Client.GetFloorRegistryAsync(A<CancellationToken>._))
            .ReturnsLazily(() => { Interlocked.Increment(ref _registryLoads); return Task.FromResult(Floors.ToArray()); });
        A.CallTo(() => Client.GetAreaRegistryAsync(A<CancellationToken>._))
            .ReturnsLazily(() => Task.FromResult(Areas.ToArray()));
        A.CallTo(() => Client.GetEntityRegistryAsync(A<CancellationToken>._))
            .ReturnsLazily(() => Task.FromResult(Entities.ToArray()));
        A.CallTo(() => Client.GetExposedEntitiesAsync(A<CancellationToken>._))
            .ReturnsLazily(() => Task.FromResult(ExposedEntityIds.ToDictionary(
                id => id,
                _ => new ExposedEntityAssistants { Conversation = true })));

        // The services request the area map as List<TheirPrivateDto>; deserialize into whatever T asks for.
        A.CallTo(Client)
            .Where(call => call.Method.Name == nameof(IHomeAssistantClient.RunTemplateAsync))
            .WithNonVoidReturnType()
            .ReturnsLazily(call =>
            {
                var resultType = call.Method.GetGenericArguments()[0];
                var json = JsonSerializer.Serialize(AreaEntities.Select(pair => new
                {
                    area_id = pair.Key,
                    area_name = Areas.First(area => area.AreaId == pair.Key).Name,
                    entities = pair.Value,
                }));
                var result = JsonSerializer.Deserialize(json, resultType);
                return typeof(Task)
                    .GetMethod(nameof(Task.FromResult))!
                    .MakeGenericMethod(resultType)
                    .Invoke(null, [result]);
            });
    }

    public IHomeAssistantClient Client { get; }

    public List<FloorRegistryEntry> Floors { get; }

    public List<AreaRegistryEntry> Areas { get; }

    public List<EntityRegistryEntry> Entities { get; }

    public Dictionary<string, List<string>> AreaEntities { get; }

    public HashSet<string> ExposedEntityIds { get; }

    /// <summary>How many times the registries were read from Home Assistant.</summary>
    public int RegistryLoadCount => Volatile.Read(ref _registryLoads);

    public static EntityRegistryEntry Entity(
        string entityId,
        string name,
        string[]? aliases = null,
        string? disabledBy = null,
        string? areaId = null) => new()
    {
        AreaId = areaId,
        EntityId = entityId,
        Name = name,
        Aliases = aliases?.ToList() ?? [],
        DisabledBy = disabledBy,
    };
}