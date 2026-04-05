using System.Collections.Immutable;

using FakeItEasy;
using lucia.Agents.Abstractions;
using lucia.Agents.Integration;
using lucia.Agents.Models;
using lucia.Agents.Models.HomeAssistant;
using lucia.Agents.Services;

namespace lucia.Tests.EntityResolution;

/// <summary>
/// End-to-end tests for the full CascadingEntityResolver pipeline.
/// Validates the cascade from query decomposition through location grounding,
/// domain filtering, and entity matching to final CascadeResult.
/// </summary>
public sealed class CascadingEntityResolverTests
{
    // ── Happy path: area + domain match → resolved ──────────────

    [Fact]
    public void Resolve_BedroomLights_TwoLightsInArea_IsResolved()
    {
        // Arrange
        var bedroomArea = CreateArea("bedroom", "Bedroom");
        var entities = new[]
        {
            CreateEntity("light.bedroom_ceiling", "Bedroom Ceiling", "bedroom"),
            CreateEntity("light.bedroom_lamp", "Bedroom Lamp", "bedroom")
        };
        var locationService = SetupLocationService(
            areas: [bedroomArea], entities: entities);

        var resolver = new CascadingEntityResolver(locationService);

        // Act — "turn on" because "on" is in IgnoreTokens; "off" leaks into location extraction
        var result = resolver.Resolve(
            "turn on the bedroom lights",
            callerArea: null,
            speakerId: null,
            domains: ["light", "switch"]);

        // Assert
        Assert.True(result.IsResolved);
        Assert.Equal("Bedroom", result.ResolvedArea);
        Assert.Equal(2, result.ResolvedEntityIds.Count);
        Assert.Contains("light.bedroom_ceiling", result.ResolvedEntityIds);
        Assert.Contains("light.bedroom_lamp", result.ResolvedEntityIds);
        Assert.Null(result.BailReason);
    }

    // ── No area match → bail NoMatch ────────────────────────────

    [Fact]
    public void Resolve_BedroomLights_NoBedroomInCache_BailsNoMatch()
    {
        // Arrange — cache has entities but no Bedroom area
        var kitchenArea = CreateArea("kitchen", "Kitchen");
        var entities = new[]
        {
            CreateEntity("light.kitchen_main", "Kitchen Main", "kitchen")
        };
        var locationService = SetupLocationService(
            areas: [kitchenArea], entities: entities);

        var resolver = new CascadingEntityResolver(locationService);

        // Act — "turn on" so decomposer extracts "bedroom" as location cleanly
        var result = resolver.Resolve(
            "turn on the bedroom lights",
            callerArea: null,
            speakerId: null,
            domains: ["light", "switch"]);

        // Assert
        Assert.False(result.IsResolved);
        Assert.Equal(BailReason.NoMatch, result.BailReason);
    }

    // ── Ambiguous: multiple areas, no caller area → bail ────────

    [Fact]
    public void Resolve_TheLamp_InThreeAreas_NoCallerArea_BailsAmbiguous()
    {
        // Arrange — "Lamp" entities exist in 3 different areas
        var kitchen = CreateArea("kitchen", "Kitchen");
        var bedroom = CreateArea("bedroom", "Bedroom");
        var living = CreateArea("living_room", "Living Room");
        var entities = new[]
        {
            CreateEntity("light.kitchen_lamp", "Lamp", "kitchen"),
            CreateEntity("light.bedroom_lamp", "Lamp", "bedroom"),
            CreateEntity("light.living_lamp", "Lamp", "living_room")
        };
        var locationService = SetupLocationService(
            areas: [kitchen, bedroom, living], entities: entities);

        var resolver = new CascadingEntityResolver(locationService);

        // Act
        var result = resolver.Resolve(
            "turn on the lamp",
            callerArea: null,
            speakerId: null,
            domains: ["light", "switch"]);

        // Assert
        Assert.False(result.IsResolved);
        Assert.Equal(BailReason.Ambiguous, result.BailReason);
    }

    // ── Disambiguation via caller area ──────────────────────────

    [Fact]
    public void Resolve_TheLamp_CallerInBedroom_ResolvesToBedroomLamp()
    {
        // Arrange
        var kitchen = CreateArea("kitchen", "Kitchen");
        var bedroom = CreateArea("bedroom", "Bedroom");
        var entities = new[]
        {
            CreateEntity("light.kitchen_lamp", "Lamp", "kitchen"),
            CreateEntity("light.bedroom_lamp", "Lamp", "bedroom")
        };
        var locationService = SetupLocationService(
            areas: [kitchen, bedroom], entities: entities);

        var resolver = new CascadingEntityResolver(locationService);

        // Act
        var result = resolver.Resolve(
            "turn on the lamp",
            callerArea: "bedroom",
            speakerId: null,
            domains: ["light", "switch"]);

        // Assert
        Assert.True(result.IsResolved);
        Assert.Single(result.ResolvedEntityIds);
        Assert.Contains("light.bedroom_lamp", result.ResolvedEntityIds);
    }

    // ── Complex command → bail immediately ──────────────────────

    [Fact]
    public void Resolve_TurnOffInFiveMinutes_BailsComplexCommand()
    {
        // Arrange
        var bedroom = CreateArea("bedroom", "Bedroom");
        var entities = new[]
        {
            CreateEntity("light.bedroom_ceiling", "Bedroom Ceiling", "bedroom")
        };
        var locationService = SetupLocationService(
            areas: [bedroom], entities: entities);

        var resolver = new CascadingEntityResolver(locationService);

        // Act
        var result = resolver.Resolve(
            "turn off lights in 5 minutes",
            callerArea: "bedroom",
            speakerId: null,
            domains: ["light", "switch"]);

        // Assert — early bail, don't even try to match entities
        Assert.False(result.IsResolved);
        Assert.Equal(BailReason.ComplexCommand, result.BailReason);
        Assert.Empty(result.ResolvedEntityIds);
    }

    // ── Cache not loaded → bail CacheNotReady ───────────────────

    [Fact]
    public void Resolve_CacheNotLoaded_BailsCacheNotReady()
    {
        // Arrange
        var locationService = A.Fake<IEntityLocationService>();
        A.CallTo(() => locationService.IsCacheReady).Returns(false);

        var resolver = new CascadingEntityResolver(locationService);

        // Act
        var result = resolver.Resolve(
            "turn off the bedroom lights",
            callerArea: null,
            speakerId: null,
            domains: ["light"]);

        // Assert
        Assert.False(result.IsResolved);
        Assert.Equal(BailReason.CacheNotReady, result.BailReason);
        Assert.Empty(result.ResolvedEntityIds);
    }

    // ── Possessive with speakerId → resolved ────────────────────

    [Fact]
    public void Resolve_TurnOffMyLight_WithSpeakerId_MatchesSpeakerEntity()
    {
        // Arrange — entity named "Zack's Light" in cache
        var entities = new[]
        {
            CreateEntity("light.zack_light", "Zack's Light", areaId: null)
        };
        var locationService = SetupLocationService(areas: [], entities: entities);

        var resolver = new CascadingEntityResolver(locationService);

        // Act — "turn on" because "on" is stripped from location extraction
        var result = resolver.Resolve(
            "turn on my light",
            callerArea: null,
            speakerId: "Zack",
            domains: ["light", "switch"]);

        // Assert
        Assert.True(result.IsResolved);
        Assert.Single(result.ResolvedEntityIds);
        Assert.Contains("light.zack_light", result.ResolvedEntityIds);
    }

    // ── Floor-level resolution ─────────────────────────────────

    [Fact]
    public void Resolve_UpstairsLights_FloorName_ExpandsToAllAreasOnFloor()
    {
        // Arrange — "Upstairs" floor with two areas
        var upstairs = CreateFloor("upstairs", "Upstairs");
        var bedroom = CreateArea("bedroom", "Bedroom", floorId: "upstairs");
        var bathroom = CreateArea("bathroom", "Bathroom", floorId: "upstairs");
        var kitchen = CreateArea("kitchen", "Kitchen");
        var entities = new[]
        {
            CreateEntity("light.bedroom_ceiling", "Bedroom Ceiling", "bedroom"),
            CreateEntity("light.bathroom_vanity", "Bathroom Vanity", "bathroom"),
            CreateEntity("light.kitchen_main", "Kitchen Main", "kitchen")
        };
        var locationService = SetupLocationService(
            areas: [bedroom, bathroom, kitchen], entities: entities, floors: [upstairs]);

        var resolver = new CascadingEntityResolver(locationService);

        // Act
        var result = resolver.Resolve(
            "turn on the upstairs lights",
            callerArea: null,
            speakerId: null,
            domains: ["light", "switch"]);

        // Assert — both upstairs area lights, not kitchen
        Assert.True(result.IsResolved);
        Assert.Equal("Upstairs", result.ResolvedFloor);
        Assert.Null(result.ResolvedArea);
        Assert.Equal(2, result.ResolvedEntityIds.Count);
        Assert.Contains("light.bedroom_ceiling", result.ResolvedEntityIds);
        Assert.Contains("light.bathroom_vanity", result.ResolvedEntityIds);
        Assert.DoesNotContain("light.kitchen_main", result.ResolvedEntityIds);
    }

    [Fact]
    public void Resolve_DownstairsLights_FloorAlias_ExpandsToAllAreasOnFloor()
    {
        // Arrange — floor with alias "downstairs"
        var ground = CreateFloor("ground_floor", "Ground Floor", aliases: ["downstairs"]);
        var living = CreateArea("living_room", "Living Room", floorId: "ground_floor");
        var dining = CreateArea("dining_room", "Dining Room", floorId: "ground_floor");
        var entities = new[]
        {
            CreateEntity("light.living_lamp", "Living Lamp", "living_room"),
            CreateEntity("light.dining_chandelier", "Dining Chandelier", "dining_room")
        };
        var locationService = SetupLocationService(
            areas: [living, dining], entities: entities, floors: [ground]);

        var resolver = new CascadingEntityResolver(locationService);

        // Act
        var result = resolver.Resolve(
            "turn on the downstairs lights",
            callerArea: null,
            speakerId: null,
            domains: ["light", "switch"]);

        // Assert
        Assert.True(result.IsResolved);
        Assert.Equal("Ground Floor", result.ResolvedFloor);
        Assert.Equal(2, result.ResolvedEntityIds.Count);
        Assert.Contains("light.living_lamp", result.ResolvedEntityIds);
        Assert.Contains("light.dining_chandelier", result.ResolvedEntityIds);
    }

    [Fact]
    public void Resolve_BedroomLights_AreaPreferredOverFloor()
    {
        // Arrange — "Bedroom" is both an area name and a floor name
        var bedroomFloor = CreateFloor("bedroom_floor", "Bedroom");
        var bedroom = CreateArea("bedroom", "Bedroom");
        var hallway = CreateArea("hallway", "Hallway", floorId: "bedroom_floor");
        var entities = new[]
        {
            CreateEntity("light.bedroom_ceiling", "Bedroom Ceiling", "bedroom"),
            CreateEntity("light.hallway_sconce", "Hallway Sconce", "hallway")
        };
        var locationService = SetupLocationService(
            areas: [bedroom, hallway], entities: entities, floors: [bedroomFloor]);

        var resolver = new CascadingEntityResolver(locationService);

        // Act
        var result = resolver.Resolve(
            "turn on the bedroom lights",
            callerArea: null,
            speakerId: null,
            domains: ["light", "switch"]);

        // Assert — area match wins over floor match
        Assert.True(result.IsResolved);
        Assert.Equal("Bedroom", result.ResolvedArea);
        Assert.Null(result.ResolvedFloor);
        Assert.Single(result.ResolvedEntityIds);
        Assert.Contains("light.bedroom_ceiling", result.ResolvedEntityIds);
    }

    [Fact]
    public void Resolve_AtticLights_FloorWithNoAreas_FallsToCallerArea()
    {
        // Arrange — "Attic" floor exists but has zero areas assigned
        var attic = CreateFloor("attic", "Attic");
        var kitchen = CreateArea("kitchen", "Kitchen");
        var entities = new[]
        {
            CreateEntity("light.kitchen_main", "Kitchen Main", "kitchen")
        };
        var locationService = SetupLocationService(
            areas: [kitchen], entities: entities, floors: [attic]);

        var resolver = new CascadingEntityResolver(locationService);

        // Act — explicit location "attic" won't match any area or populated floor
        var result = resolver.Resolve(
            "turn on the attic lights",
            callerArea: null,
            speakerId: null,
            domains: ["light", "switch"]);

        // Assert — bails because explicit location didn't resolve
        Assert.False(result.IsResolved);
        Assert.Equal(BailReason.NoMatch, result.BailReason);
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static FloorInfo CreateFloor(string floorId, string name, string[]? aliases = null) => new()
    {
        FloorId = floorId,
        Name = name,
        Aliases = aliases ?? [],
        PhoneticKeys = StringSimilarity.BuildPhoneticKeys(name),
        AliasPhoneticKeys = (aliases ?? [])
            .Select(StringSimilarity.BuildPhoneticKeys)
            .ToList()
    };

    private static AreaInfo CreateArea(string areaId, string name, string[]? aliases = null, string? floorId = null) => new()
    {
        AreaId = areaId,
        Name = name,
        FloorId = floorId,
        Aliases = aliases ?? [],
        PhoneticKeys = StringSimilarity.BuildPhoneticKeys(name),
        AliasPhoneticKeys = (aliases ?? [])
            .Select(StringSimilarity.BuildPhoneticKeys)
            .ToList()
    };

    private static HomeAssistantEntity CreateEntity(
        string entityId, string friendlyName, string? areaId) => new()
    {
        EntityId = entityId,
        FriendlyName = friendlyName,
        AreaId = areaId,
        PhoneticKeys = StringSimilarity.BuildPhoneticKeys(friendlyName),
        AliasPhoneticKeys = []
    };

    private static IEntityLocationService SetupLocationService(
        AreaInfo[] areas, HomeAssistantEntity[] entities, FloorInfo[]? floors = null)
    {
        var floorArray = floors ?? [];
        var locationService = A.Fake<IEntityLocationService>();
        A.CallTo(() => locationService.IsCacheReady).Returns(true);

        var snapshot = new LocationSnapshot(
            [.. floorArray],
            [.. areas],
            [.. entities],
            floorArray.ToImmutableDictionary(f => f.FloorId, StringComparer.OrdinalIgnoreCase),
            areas.ToImmutableDictionary(a => a.AreaId, StringComparer.OrdinalIgnoreCase),
            entities.ToImmutableDictionary(e => e.EntityId, StringComparer.OrdinalIgnoreCase));

        A.CallTo(() => locationService.GetSnapshot()).Returns(snapshot);

        A.CallTo(() => locationService.ExactMatchArea(A<string>._))
            .ReturnsLazily((string query) =>
                areas.FirstOrDefault(a =>
                    string.Equals(a.AreaId, query, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(a.Name, query, StringComparison.OrdinalIgnoreCase)));

        return locationService;
    }
}
