using FakeItEasy;
using lucia.Agents.Abstractions;
using lucia.Agents.Models.HomeAssistant;

namespace lucia.Tests.EntityResolution;

/// <summary>
/// End-to-end tests for the full CascadingEntityResolver pipeline.
/// Validates the cascade from query decomposition through location grounding,
/// domain filtering, and entity matching to final CascadeResult.
///
/// Expected interface: lucia.Agents.Services.ICascadingEntityResolver
/// Expected result: lucia.Agents.Services.CascadeResult
///   { IsResolved, ResolvedArea, ResolvedEntityIds, BailReason, Explanation }
/// Expected enum: lucia.Agents.Services.BailReason
///   { NoMatch, Ambiguous, ComplexCommand, CacheNotReady, UnsupportedIntent }
/// </summary>
public sealed class CascadingEntityResolverTests
{
    // ── Happy path: area + domain match → resolved ──────────────

    [Fact(Skip = "Pending CascadingEntityResolver implementation by Parker")]
    public void Resolve_BedroomLights_TwoLightsInArea_IsResolved()
    {
        // "turn off the bedroom lights" + "Bedroom" area has 2 light entities in cache
        // → IsResolved=true, 2 entity IDs returned
        //
        // Arrange — mock IEntityLocationService with Bedroom area + 2 lights
        // var locationService = A.Fake<IEntityLocationService>();
        // SetupCacheReady(locationService, true);
        // SetupBedroomAreaWithLights(locationService, [
        //     new HomeAssistantEntity { EntityId = "light.bedroom_ceiling", FriendlyName = "Bedroom Ceiling" },
        //     new HomeAssistantEntity { EntityId = "light.bedroom_lamp", FriendlyName = "Bedroom Lamp" }
        // ]);
        //
        // var resolver = CreateResolver(locationService);
        //
        // Act
        // var result = resolver.Resolve(
        //     "turn off the bedroom lights",
        //     callerArea: null,
        //     speakerId: null,
        //     domains: ["light", "switch"]);
        //
        // Assert
        // Assert.True(result.IsResolved);
        // Assert.Equal("Bedroom", result.ResolvedArea);
        // Assert.Equal(2, result.ResolvedEntityIds.Count);
        // Assert.Contains("light.bedroom_ceiling", result.ResolvedEntityIds);
        // Assert.Contains("light.bedroom_lamp", result.ResolvedEntityIds);
        // Assert.Null(result.BailReason);
    }

    // ── No area match → bail NoMatch ────────────────────────────

    [Fact(Skip = "Pending CascadingEntityResolver implementation by Parker")]
    public void Resolve_BedroomLights_NoBedroomInCache_BailsNoMatch()
    {
        // "turn off the bedroom lights" + no "Bedroom" area in cache
        // → IsResolved=false, BailReason=NoMatch
        //
        // Arrange — mock IEntityLocationService with no Bedroom area
        // var locationService = A.Fake<IEntityLocationService>();
        // SetupCacheReady(locationService, true);
        // A.CallTo(() => locationService.ExactMatchArea("bedroom"))
        //     .Returns((AreaInfo?)null);
        //
        // var resolver = CreateResolver(locationService);
        //
        // Act
        // var result = resolver.Resolve(
        //     "turn off the bedroom lights",
        //     callerArea: null,
        //     speakerId: null,
        //     domains: ["light", "switch"]);
        //
        // Assert
        // Assert.False(result.IsResolved);
        // Assert.Equal(BailReason.NoMatch, result.BailReason);
        // Assert.Empty(result.ResolvedEntityIds);
    }

    // ── Ambiguous: multiple areas, no caller area → bail ────────

    [Fact(Skip = "Pending CascadingEntityResolver implementation by Parker")]
    public void Resolve_TheLamp_InThreeAreas_NoCallerArea_BailsAmbiguous()
    {
        // "turn on the lamp" + "lamp" entities exist in 3 areas + no callerArea
        // → IsResolved=false, BailReason=Ambiguous (can't determine which lamp)
        //
        // Arrange — mock with lamps in Kitchen, Bedroom, and Living Room
        // var locationService = A.Fake<IEntityLocationService>();
        // SetupCacheReady(locationService, true);
        // SetupLampsInMultipleAreas(locationService);
        //
        // var resolver = CreateResolver(locationService);
        //
        // Act
        // var result = resolver.Resolve(
        //     "turn on the lamp",
        //     callerArea: null,
        //     speakerId: null,
        //     domains: ["light", "switch"]);
        //
        // Assert
        // Assert.False(result.IsResolved);
        // Assert.Equal(BailReason.Ambiguous, result.BailReason);
    }

    // ── Disambiguation via caller area ──────────────────────────

    [Fact(Skip = "Pending CascadingEntityResolver implementation by Parker")]
    public void Resolve_TheLamp_CallerInBedroom_ResolvesToBedroomLamp()
    {
        // "turn on the lamp" + callerArea="bedroom" + lamp entity in Bedroom
        // → IsResolved=true, resolved to the bedroom lamp
        //
        // Arrange
        // var locationService = A.Fake<IEntityLocationService>();
        // SetupCacheReady(locationService, true);
        // SetupBedroomAreaWithLights(locationService, [
        //     new HomeAssistantEntity { EntityId = "light.bedroom_lamp", FriendlyName = "Bedroom Lamp" }
        // ]);
        //
        // var resolver = CreateResolver(locationService);
        //
        // Act
        // var result = resolver.Resolve(
        //     "turn on the lamp",
        //     callerArea: "bedroom",
        //     speakerId: null,
        //     domains: ["light", "switch"]);
        //
        // Assert
        // Assert.True(result.IsResolved);
        // Assert.Equal(1, result.ResolvedEntityIds.Count);
        // Assert.Contains("light.bedroom_lamp", result.ResolvedEntityIds);
    }

    // ── Complex command → bail immediately ──────────────────────

    [Fact(Skip = "Pending CascadingEntityResolver implementation by Parker")]
    public void Resolve_TurnOffInFiveMinutes_BailsComplexCommand()
    {
        // "turn off lights in 5 minutes" → temporal complexity detected in Step 1
        // → IsResolved=false, BailReason=ComplexCommand (skip cascade entirely)
        //
        // Arrange
        // var locationService = A.Fake<IEntityLocationService>();
        // SetupCacheReady(locationService, true);
        //
        // var resolver = CreateResolver(locationService);
        //
        // Act
        // var result = resolver.Resolve(
        //     "turn off lights in 5 minutes",
        //     callerArea: "bedroom",
        //     speakerId: null,
        //     domains: ["light", "switch"]);
        //
        // Assert — early bail, don't even try to match entities
        // Assert.False(result.IsResolved);
        // Assert.Equal(BailReason.ComplexCommand, result.BailReason);
        // Assert.Empty(result.ResolvedEntityIds);
    }

    // ── Cache not loaded → bail CacheNotReady ───────────────────

    [Fact(Skip = "Pending CascadingEntityResolver implementation by Parker")]
    public void Resolve_CacheNotLoaded_BailsCacheNotReady()
    {
        // Entity cache has not been loaded yet → immediate bail
        //
        // Arrange
        // var locationService = A.Fake<IEntityLocationService>();
        // A.CallTo(() => locationService.IsCacheReady).Returns(false);
        //
        // var resolver = CreateResolver(locationService);
        //
        // Act
        // var result = resolver.Resolve(
        //     "turn off the bedroom lights",
        //     callerArea: null,
        //     speakerId: null,
        //     domains: ["light"]);
        //
        // Assert
        // Assert.False(result.IsResolved);
        // Assert.Equal(BailReason.CacheNotReady, result.BailReason);
        // Assert.Empty(result.ResolvedEntityIds);
    }

    // ── Possessive with speakerId → resolved ────────────────────

    [Fact(Skip = "Pending CascadingEntityResolver implementation by Parker")]
    public void Resolve_TurnOffMyLight_WithSpeakerId_MatchesSpeakerEntity()
    {
        // "turn off my light" + speakerId="Zack" + entity "Zack's Light" in cache
        // → IsResolved=true, resolved to "Zack's Light" entity
        //
        // Arrange
        // var locationService = A.Fake<IEntityLocationService>();
        // SetupCacheReady(locationService, true);
        // — mock entity with FriendlyName = "Zack's Light" visible in cache
        //
        // var resolver = CreateResolver(locationService);
        //
        // Act
        // var result = resolver.Resolve(
        //     "turn off my light",
        //     callerArea: null,
        //     speakerId: "Zack",
        //     domains: ["light", "switch"]);
        //
        // Assert
        // Assert.True(result.IsResolved);
        // Assert.Equal(1, result.ResolvedEntityIds.Count);
        // Assert.NotNull(result.Explanation);
    }
}
