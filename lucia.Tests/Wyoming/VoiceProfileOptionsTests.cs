using lucia.Wyoming.Diarization;

namespace lucia.Tests.Wyoming;

public sealed class VoiceProfileOptionsTests
{
    [Theory]
    [InlineData(0.10f)]
    [InlineData(0.35f)]
    [InlineData(0.95f)]
    public void IsValidSpeakerVerificationThreshold_AllowsFiniteRangeValues(float value)
    {
        Assert.True(VoiceProfileOptions.IsValidSpeakerVerificationThreshold(value));
    }

    [Theory]
    [InlineData(0.09f)]
    [InlineData(0.00f)]
    [InlineData(1.0f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void IsValidSpeakerVerificationThreshold_RejectsNonFiniteOrOutOfRangeValues(float value)
    {
        Assert.False(VoiceProfileOptions.IsValidSpeakerVerificationThreshold(value));
    }

    [Theory]
    [InlineData(0.10f)]
    [InlineData(0.65f)]
    [InlineData(0.90f)]
    public void IsValidProvisionalMatchThreshold_AllowsFiniteRangeValues(float value)
    {
        Assert.True(VoiceProfileOptions.IsValidProvisionalMatchThreshold(value));
    }

    [Theory]
    [InlineData(0.09f)]
    [InlineData(0.00f)]
    [InlineData(1.0f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void IsValidProvisionalMatchThreshold_RejectsNonFiniteOrOutOfRangeValues(float value)
    {
        Assert.False(VoiceProfileOptions.IsValidProvisionalMatchThreshold(value));
    }
}
