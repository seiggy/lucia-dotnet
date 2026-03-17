using System.Buffers.Binary;

namespace lucia.Wyoming.Audio;

/// <summary>
/// Writes 16-bit PCM mono WAV files from float32 audio samples.
/// </summary>
public static class WavWriter
{
    public static async Task WriteAsync(
        string filePath,
        ReadOnlyMemory<float> samples,
        int sampleRate,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);

        if (samples.IsEmpty)
        {
            throw new ArgumentException("Audio samples cannot be empty.", nameof(samples));
        }

        var parentDir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(parentDir))
        {
            Directory.CreateDirectory(parentDir);
        }

        var numSamples = samples.Length;
        var dataSize = numSamples * 2; // 16-bit = 2 bytes per sample
        var fileSize = 44 + dataSize; // WAV header is 44 bytes

        await using var stream = File.Create(filePath);
        var header = new byte[44];

        // RIFF header
        "RIFF"u8.CopyTo(header.AsSpan(0));
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), fileSize - 8);
        "WAVE"u8.CopyTo(header.AsSpan(8));

        // fmt sub-chunk
        "fmt "u8.CopyTo(header.AsSpan(12));
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(16), 16); // PCM chunk size
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(20), 1); // PCM format
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(22), 1); // mono
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(24), sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(28), sampleRate * 2); // byte rate
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(32), 2); // block align
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(34), 16); // bits per sample

        // data sub-chunk
        "data"u8.CopyTo(header.AsSpan(36));
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(40), dataSize);

        await stream.WriteAsync(header, ct).ConfigureAwait(false);

        // Convert float32 [-1.0, 1.0] to int16 and write in chunks
        const int chunkSize = 4096;
        var pcmBuffer = new byte[chunkSize * 2];

        for (var offset = 0; offset < numSamples; offset += chunkSize)
        {
            var count = Math.Min(chunkSize, numSamples - offset);
            ConvertChunk(samples, offset, count, pcmBuffer);
            await stream.WriteAsync(pcmBuffer.AsMemory(0, count * 2), ct).ConfigureAwait(false);
        }
    }

    private static void ConvertChunk(
        ReadOnlyMemory<float> samples, int offset, int count, byte[] pcmBuffer)
    {
        var span = samples.Span;
        for (var i = 0; i < count; i++)
        {
            var clamped = Math.Clamp(span[offset + i], -1.0f, 1.0f);
            var pcmValue = (short)(clamped * short.MaxValue);
            BinaryPrimitives.WriteInt16LittleEndian(pcmBuffer.AsSpan(i * 2), pcmValue);
        }
    }
}
