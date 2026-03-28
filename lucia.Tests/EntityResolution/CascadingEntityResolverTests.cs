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
