using FakeItEasy;
using lucia.Agents.Abstractions;
using lucia.Agents.Models.HomeAssistant;

namespace lucia.Tests.EntityResolution;

/// <summary>
/// Tests for Step 2 of the cascade: Location Grounding.
/// Validates the priority order: explicit location > caller area > null.
///
/// Expected behavior:
/// 1. Explicit location in query overrides callerArea
/// 2. callerArea used when no explicit location in query
/// 3. null returned when neither is available
/// 4. Case-insensitive matching
/// 5. Alias matching (e.g., "front room" → "Living Room")
/// </summary>
public sealed class LocationGroundingTests
{
    // ── Explicit location overrides caller area ─────────────────

    [Fact(Skip = "Pending CascadingEntityResolver implementation by Parker")]
    public void GroundLocation_ExplicitBedroom_WithCallerKitchen_ResolvesBedroom()
    {
        // Query says "bedroom" explicitly, caller device is in "kitchen".
        // Explicit location in the query must override the caller's area.
        //
        // Arrange
        // var locationService = A.Fake<IEntityLocationService>();
        // A.CallTo(() => locationService.ExactMatchArea("bedroom"))
        //     .Returns(new AreaInfo { AreaId = "bedroom", Name = "Bedroom" });
        //
        // var resolver = CreateResolver(locationService);
        //
        // Act — resolve "turn off the bedroom lights" with callerArea="kitchen"
        // var result = resolver.Resolve(
        //     "turn off the bedroom lights",
        //     callerArea: "kitchen",
        //     speakerId: null,
        //     domains: ["light"]);
        //
        // Assert — resolved area should be "Bedroom", not "Kitchen"
        // Assert.True(result.IsResolved || result.BailReason == BailReason.NoMatch);
        // Assert.Equal("Bedroom", result.ResolvedArea);
    }

    // ── Caller area used when no explicit location ──────────────

    [Fact(Skip = "Pending CascadingEntityResolver implementation by Parker")]
    public void GroundLocation_NoExplicitLocation_UsesCallerArea()
    {
        // Query is "turn off the lights" — no area mentioned.
        // callerArea="kitchen" should be used for grounding.
        //
        // Arrange
        // var locationService = A.Fake<IEntityLocationService>();
        // A.CallTo(() => locationService.ExactMatchArea("kitchen"))
        //     .Returns(new AreaInfo { AreaId = "kitchen", Name = "Kitchen" });
        //
        // var resolver = CreateResolver(locationService);
        //
        // Act
        // var result = resolver.Resolve(
        //     "turn off the lights",
        //     callerArea: "kitchen",
        //     speakerId: null,
        //     domains: ["light"]);
        //
        // Assert — callerArea becomes the resolved area
        // Assert.Equal("Kitchen", result.ResolvedArea);
    }

    // ── No location at all → null area (wider search) ───────────

    [Fact(Skip = "Pending CascadingEntityResolver implementation by Parker")]
    public void GroundLocation_NoExplicitLocation_NoCallerArea_ReturnsNullArea()
    {
        // No location in query + callerArea is null → null area.
        // This means wider search or bail if ambiguous.
        //
        // Arrange
        // var resolver = CreateResolver(A.Fake<IEntityLocationService>());
        //
        // Act
        // var result = resolver.Resolve(
        //     "turn off the lights",
        //     callerArea: null,
        //     speakerId: null,
        //     domains: ["light"]);
        //
        // Assert — either resolves broadly or bails with Ambiguous
        // Assert.Null(result.ResolvedArea);
    }

    // ── Area alias matching ─────────────────────────────────────

    [Fact(Skip = "Pending CascadingEntityResolver implementation by Parker")]
    public void GroundLocation_FrontRoom_MatchesAlias_ResolvesToLivingRoom()
    {
        // "front room" is an alias for "Living Room" in HA area config.
        // The cascade should check area aliases in Step 2.
        //
        // Arrange
        // var locationService = A.Fake<IEntityLocationService>();
        // A.CallTo(() => locationService.ExactMatchArea("front room")).Returns((AreaInfo?)null);
        // — but the actual AreaInfo has Aliases = ["front room"]
        //
        // var resolver = CreateResolver(locationService);
        //
        // Act
        // var result = resolver.Resolve(
        //     "turn on the front room lights",
        //     callerArea: null,
        //     speakerId: null,
        //     domains: ["light"]);
        //
        // Assert — resolved to Living Room via alias
        // Assert.Equal("Living Room", result.ResolvedArea);
    }

    // ── Case-insensitive matching ───────────────────────────────

    [Fact(Skip = "Pending CascadingEntityResolver implementation by Parker")]
    public void GroundLocation_UppercaseBEDROOM_MatchesBedroomArea()
    {
        // "BEDROOM" in query should match area "Bedroom" (case-insensitive).
        //
        // Arrange
        // var locationService = A.Fake<IEntityLocationService>();
        // A.CallTo(() => locationService.ExactMatchArea("BEDROOM"))
        //     .Returns(new AreaInfo { AreaId = "bedroom", Name = "Bedroom" });
        //
        // var resolver = CreateResolver(locationService);
        //
        // Act
        // var result = resolver.Resolve(
        //     "turn off the BEDROOM lights",
        //     callerArea: null,
        //     speakerId: null,
        //     domains: ["light"]);
        //
        // Assert — case-insensitive match succeeded
        // Assert.Equal("Bedroom", result.ResolvedArea);
    }
}
