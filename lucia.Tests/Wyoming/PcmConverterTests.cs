using lucia.Wyoming.Audio;

namespace lucia.Tests.Wyoming;

public sealed class PcmConverterTests
{
    [Fact]
    public void Int16ToFloat32_ConvertsCorrectly()
    {
        var pcm = new byte[] { 0xFF, 0x7F, 0x00, 0x00, 0x01, 0x80 };

        var floats = PcmConverter.Int16ToFloat32(pcm);

        Assert.Equal(3, floats.Length);
        Assert.True(floats[0] > 0.99f);
        Assert.Equal(0f, floats[1]);
        Assert.True(floats[2] < -0.99f);
    }

    [Fact]
    public void Float32ToInt16_ConvertsCorrectly()
    {
        var floats = new float[] { 1.0f, 0.0f, -1.0f };

        var pcm = PcmConverter.Float32ToInt16(floats);

        Assert.Equal(6, pcm.Length);
        Assert.Equal(new byte[] { 0xFF, 0x7F, 0x00, 0x00, 0x00, 0x80 }, pcm);
    }

    [Fact]
    public void RoundTrip_PreservesData()
    {
        var original = new float[] { 0.5f, -0.5f, 0.0f, 0.25f, -0.75f };

        var pcm = PcmConverter.Float32ToInt16(original);
        var restored = PcmConverter.Int16ToFloat32(pcm);

        Assert.Equal(original.Length, restored.Length);

        for (var i = 0; i < original.Length; i++)
        {
            Assert.True(
                Math.Abs(original[i] - restored[i]) < 0.001f,
                $"Sample {i}: expected {original[i]}, got {restored[i]}");
        }
    }

    [Fact]
    public void Int16ToFloat32_EmptyInput_ReturnsEmpty()
    {
        var result = PcmConverter.Int16ToFloat32([]);

        Assert.Empty(result);
    }

    [Fact]
    public void Int16ToFloat32_OddByteCount_Throws()
    {
        Assert.Throws<ArgumentException>(() => PcmConverter.Int16ToFloat32(new byte[] { 0x01 }));
    }

    [Fact]
    public void Float32ToInt16_ClampsOutOfRange()
    {
        var floats = new float[] { 1.5f, -1.5f };

        var pcm = PcmConverter.Float32ToInt16(floats);
        var restored = PcmConverter.Int16ToFloat32(pcm);

        Assert.True(restored[0] >= 0.99f);
        Assert.True(restored[1] <= -0.99f);
    }
}
