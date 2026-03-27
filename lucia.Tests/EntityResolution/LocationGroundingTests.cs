using System.Collections.Immutable;

using FakeItEasy;
using lucia.Agents.Abstractions;
using lucia.Agents.Integration;
using lucia.Agents.Models;
using lucia.Agents.Models.HomeAssistant;
using lucia.Agents.Services;

namespace lucia.Tests.EntityResolution;

/// <summary>
/// Tests for Step 2 of the cascade: Location Grounding.
/// Validates the priority order: explicit location > caller area > null.
/// </summary>
public sealed class LocationGroundingTests
{
    // ── Explicit location overrides caller area ─────────────────

    [Fact]
    public void GroundLocation_ExplicitBedroom_WithCallerKitchen_ResolvesBedroom()
    {
        // Arrange
        var bedroom = CreateArea("bedroom", "Bedroom");
        var kitchen = CreateArea("kitchen", "Kitchen");
        var entities = new[]
        {
            CreateEntity("light.bedroom_ceiling", "Bedroom Ceiling", "bedroom"),
            CreateEntity("light.kitchen_main", "Kitchen Main", "kitchen")
        };
        var locationService = SetupLocationService(
            areas: [bedroom, kitchen], entities: entities);

        var resolver = new CascadingEntityResolver(locationService);

        // Act — "turn on" so decomposer extracts "bedroom" cleanly
        var result = resolver.Resolve(
            "turn on the bedroom lights",
            callerArea: "kitchen",
            speakerId: null,
            domains: ["light"]);

        // Assert — resolved area should be "Bedroom", not "Kitchen"
        Assert.Equal("Bedroom", result.ResolvedArea);
    }

    // ── Caller area used when no explicit location ──────────────

    [Fact]
    public void GroundLocation_NoExplicitLocation_UsesCallerArea()
    {
        // Arrange
        var kitchen = CreateArea("kitchen", "Kitchen");
        var entities = new[]
        {
            CreateEntity("light.kitchen_main", "Kitchen Main", "kitchen")
        };
        var locationService = SetupLocationService(
            areas: [kitchen], entities: entities);

        var resolver = new CascadingEntityResolver(locationService);

        // Act — "turn off the lights" has no explicit area
        var result = resolver.Resolve(
            "turn off the lights",
            callerArea: "kitchen",
            speakerId: null,
            domains: ["light"]);

        // Assert — callerArea becomes the resolved area
        Assert.Equal("Kitchen", result.ResolvedArea);
    }

    // ── No location at all → null area (wider search) ───────────

    [Fact]
    public void GroundLocation_NoExplicitLocation_NoCallerArea_ReturnsNullArea()
    {
        // Arrange — single entity so it won't be ambiguous
        var entities = new[]
        {
            CreateEntity("light.main", "Main Light", areaId: null)
        };
        var locationService = SetupLocationService(areas: [], entities: entities);

        var resolver = new CascadingEntityResolver(locationService);

        // Act
        var result = resolver.Resolve(
            "turn off the lights",
            callerArea: null,
            speakerId: null,
            domains: ["light"]);

        // Assert — no area context available
        Assert.Null(result.ResolvedArea);
    }

    // ── Area alias matching ─────────────────────────────────────

    [Fact]
    public void GroundLocation_FrontRoom_MatchesAlias_ResolvesToLivingRoom()
    {
        // Arrange — "Living Room" area has alias "front room"
        var livingRoom = CreateArea("living_room", "Living Room",
            aliases: ["front room"]);
        var entities = new[]
        {
            CreateEntity("light.living_lamp", "Living Room Lamp", "living_room")
        };
        var locationService = SetupLocationService(
            areas: [livingRoom], entities: entities);

        // ExactMatchArea("front room") returns null (no exact area name match),
        // but alias matching should find "Living Room"
        A.CallTo(() => locationService.ExactMatchArea("front room"))
            .Returns((AreaInfo?)null);

        var resolver = new CascadingEntityResolver(locationService);

        // Act
        var result = resolver.Resolve(
            "turn on the front room lights",
            callerArea: null,
            speakerId: null,
            domains: ["light"]);

        // Assert — resolved to Living Room via alias
        Assert.Equal("Living Room", result.ResolvedArea);
    }

    // ── Case-insensitive matching ───────────────────────────────

    [Fact]
    public void GroundLocation_UppercaseBEDROOM_MatchesBedroomArea()
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
            "turn on the BEDROOM lights",
            callerArea: null,
            speakerId: null,
            domains: ["light"]);

        // Assert — case-insensitive match succeeded
        Assert.Equal("Bedroom", result.ResolvedArea);
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static AreaInfo CreateArea(string areaId, string name, string[]? aliases = null) => new()
    {
        AreaId = areaId,
        Name = name,
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
        AreaInfo[] areas, HomeAssistantEntity[] entities)
    {
        var locationService = A.Fake<IEntityLocationService>();
        A.CallTo(() => locationService.IsCacheReady).Returns(true);

        var snapshot = new LocationSnapshot(
            ImmutableArray<FloorInfo>.Empty,
            [.. areas],
            [.. entities],
            ImmutableDictionary<string, FloorInfo>.Empty,
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
