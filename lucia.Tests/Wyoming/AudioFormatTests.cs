using lucia.Wyoming.Audio;

namespace lucia.Tests.Wyoming;

public sealed class AudioFormatTests
{
    [Fact]
    public void Default_HasWyomingPcmDefaults()
    {
        var format = AudioFormat.Default;

        Assert.Equal(16_000, format.SampleRate);
        Assert.Equal(16, format.BitsPerSample);
        Assert.Equal(1, format.Channels);
    }

    [Fact]
    public void BytesPerSecond_ComputesExpectedValue()
    {
        var format = AudioFormat.Default;

        Assert.Equal(32_000, format.BytesPerSecond);
    }

    [Fact]
    public void BytesPerFrame_ComputesExpectedValue()
    {
        var format = AudioFormat.Default;

        Assert.Equal(2, format.BytesPerFrame);
    }
}
