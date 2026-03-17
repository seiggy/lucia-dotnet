namespace lucia.Wyoming.Audio;

using System.Buffers.Binary;

/// <summary>
/// Converts between PCM byte buffers and normalized float samples.
/// </summary>
public static class PcmConverter
{
    /// <summary>
    /// Converts 16-bit little-endian PCM bytes to normalized float32 samples in the range [-1.0, 1.0].
    /// </summary>
    public static float[] Int16ToFloat32(ReadOnlySpan<byte> pcm)
    {
        if (pcm.Length % sizeof(short) != 0)
        {
            throw new ArgumentException("PCM data length must be divisible by 2 bytes.", nameof(pcm));
        }

        var sampleCount = pcm.Length / sizeof(short);
        var samples = new float[sampleCount];

        for (var i = 0; i < sampleCount; i++)
        {
            var offset = i * sizeof(short);
            var value = BinaryPrimitives.ReadInt16LittleEndian(pcm.Slice(offset, sizeof(short)));
            samples[i] = value / 32768f;
        }

        return samples;
    }

    /// <summary>
    /// Converts normalized float32 samples in the range [-1.0, 1.0] to 16-bit little-endian PCM bytes.
    /// </summary>
    public static byte[] Float32ToInt16(ReadOnlySpan<float> samples)
    {
        var pcm = new byte[samples.Length * sizeof(short)];

        for (var i = 0; i < samples.Length; i++)
        {
            var clamped = Math.Clamp(samples[i], -1.0f, 1.0f);
            short value = clamped switch
            {
                <= -1.0f => short.MinValue,
                >= 1.0f => short.MaxValue,
                _ => (short)MathF.Round(clamped * short.MaxValue),
            };

            var offset = i * sizeof(short);
            BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(offset, sizeof(short)), value);
        }

        return pcm;
    }
}
