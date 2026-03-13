using lucia.Wyoming.Audio;

namespace lucia.Tests.Wyoming;

public sealed class AudioResamplerTests
{
    [Fact]
    public void Resample_WhenRatesMatch_ReturnsEquivalentSamples()
    {
        var input = Enumerable.Range(0, 32).Select(static index => index / 32f).ToArray();

        var output = AudioResampler.Resample(input, 16_000, 16_000);

        Assert.Equal(input, output);
    }

    [Fact]
    public void Resample_From48kHzTo16kHz_DownsamplesToExpectedLength()
    {
        var input = Enumerable.Range(0, 480).Select(static index => (float)Math.Sin(index / 20d)).ToArray();

        var output = AudioResampler.Resample(input, 48_000, 16_000);
        var expectedLength = (int)Math.Round(input.Length * (double)16_000 / 48_000);

        Assert.InRange(Math.Abs(output.Length - expectedLength), 0, 1);
        Assert.True(output.Length < input.Length);
    }

    [Fact]
    public void Resample_From16kHzTo24kHz_UpsamplesToExpectedLength()
    {
        var input = Enumerable.Range(0, 160).Select(static index => index / 10f).ToArray();

        var output = AudioResampler.Resample(input, 16_000, 24_000);
        var expectedLength = (int)Math.Round(input.Length * (double)24_000 / 16_000);

        Assert.InRange(Math.Abs(output.Length - expectedLength), 0, 1);
        Assert.True(output.Length > input.Length);
    }

    [Fact]
    public void Resample_EmptyInput_ReturnsEmptyOutput()
    {
        var output = AudioResampler.Resample([], 16_000, 24_000);

        Assert.Empty(output);
    }

    [Fact]
    public void Resample_OutputLength_IsWithinOneSampleOfExpected()
    {
        var input = Enumerable.Range(0, 97).Select(static index => (float)Math.Cos(index / 5d)).ToArray();
        var inputRate = 22_050;
        var outputRate = 16_000;

        var output = AudioResampler.Resample(input, inputRate, outputRate);
        var expectedLength = (int)Math.Round(input.Length * (double)outputRate / inputRate);

        Assert.InRange(Math.Abs(output.Length - expectedLength), 0, 1);
    }
}
