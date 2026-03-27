using FakeItEasy;

namespace lucia.Tests.EntityResolution;

/// <summary>
/// Tests for the QueryDecomposer — Step 1 of the cascading entity resolution pipeline.
/// Validates extraction of action, location, device type, and complexity detection.
/// 
/// Expected type: lucia.Agents.Services.QueryDecomposer
/// Expected output: lucia.Agents.Services.QueryIntent record
///   { Action, ExplicitLocation, DeviceType, IsComplex, ComplexityReason }
/// </summary>
public sealed class QueryDecomposerTests
{
    // ── Simple command decomposition ────────────────────────────

    [Fact(Skip = "Pending CascadingEntityResolver implementation by Parker")]
    public void Decompose_TurnOffBedroomLights_ExtractsActionLocationAndDeviceType()
    {
        // Arrange
        // var decomposer = new QueryDecomposer();

        // Act
        // var intent = decomposer.Decompose("turn off the bedroom lights", speakerId: null);

        // Assert
        // Assert.Equal("off", intent.Action);
        // Assert.Equal("bedroom", intent.ExplicitLocation);
        // Assert.Equal("lights", intent.DeviceType);
        // Assert.False(intent.IsComplex);
        // Assert.Null(intent.ComplexityReason);
    }

    [Fact(Skip = "Pending CascadingEntityResolver implementation by Parker")]
    public void Decompose_TurnOnKitchenLight_ExtractsLocationNoPossessive()
    {
        // "turn on the kitchen light" — no possessive expansion needed
        // Arrange
        // var decomposer = new QueryDecomposer();

        // Act
        // var intent = decomposer.Decompose("turn on the kitchen light", speakerId: null);

        // Assert
        // Assert.Equal("on", intent.Action);
        // Assert.Equal("kitchen", intent.ExplicitLocation);
        // Assert.Equal("light", intent.DeviceType);
        // Assert.False(intent.IsComplex);
        // Assert.Null(intent.ComplexityReason);
    }

    // ── Complexity detection: temporal ──────────────────────────

    [Fact(Skip = "Pending CascadingEntityResolver implementation by Parker")]
    public void Decompose_InFiveMinutes_DetectsTemporalComplexity()
    {
        // "turn on the lights in 5 minutes" — temporal modifier → complex
        // Arrange
        // var decomposer = new QueryDecomposer();

        // Act
        // var intent = decomposer.Decompose("turn on the lights in 5 minutes", speakerId: null);

        // Assert
        // Assert.True(intent.IsComplex);
        // Assert.Equal("temporal", intent.ComplexityReason);
    }

    // ── Complexity detection: color ─────────────────────────────

    [Fact(Skip = "Pending CascadingEntityResolver implementation by Parker")]
    public void Decompose_SetLightsToBlue_DetectsColorComplexity()
    {
        // "set the lights to blue" — color command → complex (requires LLM for parameter extraction)
        // Arrange
        // var decomposer = new QueryDecomposer();

        // Act
        // var intent = decomposer.Decompose("set the lights to blue", speakerId: null);

        // Assert
        // Assert.True(intent.IsComplex);
        // Assert.Equal("color", intent.ComplexityReason);
    }

    // ── Complexity detection: conjunction ────────────────────────

    [Fact(Skip = "Pending CascadingEntityResolver implementation by Parker")]
    public void Decompose_TurnOnLightsAndPlayMusic_DetectsConjunctionComplexity()
    {
        // "turn on lights and play music" — conjunction → complex (multi-intent)
        // Arrange
        // var decomposer = new QueryDecomposer();

        // Act
        // var intent = decomposer.Decompose("turn on lights and play music", speakerId: null);

        // Assert
        // Assert.True(intent.IsComplex);
        // Assert.Equal("conjunction", intent.ComplexityReason);
    }

    // ── Possessive expansion with speakerId ─────────────────────

    [Fact(Skip = "Pending CascadingEntityResolver implementation by Parker")]
    public void Decompose_MyLight_WithSpeakerId_ExpandsPossessive()
    {
        // "turn off my light" + speakerId="Zack" → possessive expanded to
        // candidate names ["Zack's Light", "Zack Light"]
        // Arrange
        // var decomposer = new QueryDecomposer();

        // Act
        // var intent = decomposer.Decompose("turn off my light", speakerId: "Zack");

        // Assert — possessive candidates should include speaker name variants
        // Assert.Equal("off", intent.Action);
        // Assert.NotNull(intent.PossessiveCandidates);
        // Assert.Contains("Zack's Light", intent.PossessiveCandidates);
        // Assert.Contains("Zack Light", intent.PossessiveCandidates);
        // Assert.False(intent.IsComplex);
    }

    [Fact(Skip = "Pending CascadingEntityResolver implementation by Parker")]
    public void Decompose_LightsInMyOffice_WithSpeakerId_ExpandsAreaPossessive()
    {
        // "lights in my office" + speakerId="Zack" → area candidates
        // include ["Zack's Office", "Zack Office"]
        // Arrange
        // var decomposer = new QueryDecomposer();

        // Act
        // var intent = decomposer.Decompose("lights in my office", speakerId: "Zack");

        // Assert — area candidates should include speaker-qualified variants
        // Assert.NotNull(intent.AreaCandidates);
        // Assert.Contains("Zack's Office", intent.AreaCandidates);
        // Assert.Contains("Zack Office", intent.AreaCandidates);
    }
}
