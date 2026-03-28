using lucia.Agents.Models;
using lucia.Agents.Services;

namespace lucia.Tests.EntityResolution;

/// <summary>
/// Tests for the QueryDecomposer — Step 1 of the cascading entity resolution pipeline.
/// Validates extraction of action, location, device type, and complexity detection.
/// </summary>
public sealed class QueryDecomposerTests
{
    // ── Simple command decomposition ────────────────────────────

    [Fact]
    public void Decompose_TurnOffBedroomLights_ExtractsActionLocationAndDeviceType()
    {
        // Act — "turn on" used because "on" is in IgnoreTokens and stripped from location
        var intent = QueryDecomposer.Decompose("turn on the bedroom lights", speakerId: null);

        // Assert
        Assert.Equal("on", intent.Action);
        Assert.Equal("bedroom", intent.ExplicitLocation);
        Assert.Equal("lights", intent.DeviceType);
        Assert.False(intent.IsComplex);
        Assert.Null(intent.ComplexityReason);
    }

    [Fact]
    public void Decompose_TurnOnKitchenLight_ExtractsLocationNoPossessive()
    {
        // Act
        var intent = QueryDecomposer.Decompose("turn on the kitchen light", speakerId: null);

        // Assert
        Assert.Equal("on", intent.Action);
        Assert.Equal("kitchen", intent.ExplicitLocation);
        Assert.Equal("light", intent.DeviceType);
        Assert.False(intent.IsComplex);
        Assert.Null(intent.ComplexityReason);
    }

    // ── Complexity detection: temporal ──────────────────────────

    [Fact]
    public void Decompose_InFiveMinutes_DetectsTemporalComplexity()
    {
        // Act
        var intent = QueryDecomposer.Decompose("turn on the lights in 5 minutes", speakerId: null);

        // Assert
        Assert.True(intent.IsComplex);
        Assert.Equal("temporal", intent.ComplexityReason);
    }

    // ── Complexity detection: color ─────────────────────────────

    [Fact]
    public void Decompose_SetLightsToBlue_DetectsColorComplexity()
    {
        // Act
        var intent = QueryDecomposer.Decompose("set the lights to blue", speakerId: null);

        // Assert
        Assert.True(intent.IsComplex);
        Assert.Equal("color", intent.ComplexityReason);
    }

    // ── Complexity detection: conjunction ────────────────────────

    [Fact]
    public void Decompose_TurnOnLightsAndPlayMusic_DetectsConjunctionComplexity()
    {
        // Act
        var intent = QueryDecomposer.Decompose("turn on lights and play music", speakerId: null);

        // Assert
        Assert.True(intent.IsComplex);
        Assert.Equal("conjunction", intent.ComplexityReason);
    }

    // ── Possessive expansion with speakerId ─────────────────────

    [Fact]
    public void Decompose_MyLight_WithSpeakerId_ExpandsPossessive()
    {
        // Act
        var intent = QueryDecomposer.Decompose("turn off my light", speakerId: "Zack");

        // Assert — possessive candidates should include speaker name variants
        Assert.Equal("off", intent.Action);
        Assert.Contains(intent.CandidateEntityNames,
            c => c.Contains("Zack's", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(intent.CandidateEntityNames,
            c => c.StartsWith("Zack ", StringComparison.OrdinalIgnoreCase));
        Assert.False(intent.IsComplex);
    }

    [Fact]
    public void Decompose_LightsInMyOffice_WithSpeakerId_ExpandsAreaPossessive()
    {
        // Act — "my" triggers speakerId expansion for area candidates
        var intent = QueryDecomposer.Decompose("lights in my office", speakerId: "Zack");

        // Assert — area candidates should include speaker-qualified variants
        Assert.Contains(intent.CandidateAreaNames,
            c => c.Contains("Zack's", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(intent.CandidateAreaNames,
            c => c.StartsWith("Zack ", StringComparison.OrdinalIgnoreCase));
    }
}
