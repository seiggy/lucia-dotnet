using System.Buffers.Binary;
using lucia.Wyoming.Audio;

namespace lucia.Tests.Wyoming;

public sealed class WavWriterTests
{
    [Fact]
    public async Task WriteAsync_ProducesValidWavHeader()
    {
        var path = Path.GetTempFileName();
        try
        {
            const int sampleRate = 16000;
            var samples = new float[1000];
            Array.Fill(samples, 0.5f);

            await WavWriter.WriteAsync(path, samples, sampleRate);

            var bytes = await File.ReadAllBytesAsync(path);

            // RIFF header
            Assert.Equal("RIFF"u8.ToArray(), bytes[..4]);
            var fileSize = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4));
            Assert.Equal(bytes.Length - 8, fileSize);
            Assert.Equal("WAVE"u8.ToArray(), bytes[8..12]);

            // fmt sub-chunk
            Assert.Equal("fmt "u8.ToArray(), bytes[12..16]);
            var fmtChunkSize = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(16));
            Assert.Equal(16, fmtChunkSize);

            var audioFormat = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(20));
            Assert.Equal(1, audioFormat); // PCM

            var channels = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(22));
            Assert.Equal(1, channels); // mono

            var readSampleRate = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(24));
            Assert.Equal(sampleRate, readSampleRate);

            // bits per sample — standard WAV offset 34
            var bitsPerSample = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(34));
            Assert.Equal(16, bitsPerSample);

            // data sub-chunk
            Assert.Equal("data"u8.ToArray(), bytes[36..40]);
            var dataSize = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(40));
            Assert.Equal(1000 * 2, dataSize);

            // Total file size: 44 header + 2000 data
            Assert.Equal(44 + 2000, bytes.Length);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task WriteAsync_CorrectlyClampsValues()
    {
        var path = Path.GetTempFileName();
        try
        {
            float[] samples = [2.0f, -2.0f, 0.5f];

            await WavWriter.WriteAsync(path, samples, 16000);

            var bytes = await File.ReadAllBytesAsync(path);

            // Read back PCM values from data section (offset 44)
            var pcm0 = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(44));
            var pcm1 = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(46));
            var pcm2 = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(48));

            // 2.0f clamped to 1.0f → short.MaxValue (32767)
            Assert.Equal(short.MaxValue, pcm0);

            // -2.0f clamped to -1.0f → -short.MaxValue (-32767)
            Assert.Equal(-short.MaxValue, pcm1);

            // 0.5f → ~16383
            Assert.Equal((short)(0.5f * short.MaxValue), pcm2);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task WriteAsync_EmptySamples_Throws()
    {
        var path = Path.GetTempFileName();
        try
        {
            await Assert.ThrowsAsync<ArgumentException>(
                () => WavWriter.WriteAsync(path, ReadOnlyMemory<float>.Empty, 16000));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task WriteAsync_ZeroSampleRate_Throws()
    {
        var path = Path.GetTempFileName();
        try
        {
            var samples = new float[100];
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => WavWriter.WriteAsync(path, samples, 0));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
