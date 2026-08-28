using System.Text;

namespace lucia.VoiceBenchmarks.Tests;

public sealed class AudioWaveLoaderTests
{
    [Fact]
    public void LoadMono16KhzFloatSamples_PreservesMono16KhzSamples()
    {
        var path = WritePcm16Wave(16000, 1, [0, 16384, -16384]);
        try
        {
            var samples = AudioWaveLoader.LoadMono16KhzFloatSamples(path);

            Assert.Equal(3, samples.Length);
            Assert.Equal(0f, samples[0]);
            Assert.InRange(samples[1], 0.49f, 0.51f);
            Assert.InRange(samples[2], -0.51f, -0.49f);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadMono16KhzFloatSamples_DownmixesStereo()
    {
        var path = WritePcm16Wave(16000, 2, [short.MaxValue, short.MinValue, 16384, 16384]);
        try
        {
            var samples = AudioWaveLoader.LoadMono16KhzFloatSamples(path);

            Assert.Equal(2, samples.Length);
            Assert.InRange(samples[0], -0.001f, 0.001f);
            Assert.InRange(samples[1], 0.49f, 0.51f);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadMono16KhzFloatSamples_ResamplesTo16KhzWithoutChangingDuration()
    {
        var path = WritePcm16Wave(8000, 1, Enumerable.Repeat((short)8192, 8000).ToArray());
        try
        {
            var samples = AudioWaveLoader.LoadMono16KhzFloatSamples(path);

            Assert.InRange(samples.Length, 15990, 16010);
            Assert.InRange(samples.Length / 16000d, 0.999d, 1.001d);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string WritePcm16Wave(int sampleRate, short channels, short[] samples)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lucia-wave-{Guid.NewGuid():N}.wav");
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream, Encoding.ASCII);
        var dataLength = samples.Length * sizeof(short);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataLength);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * sizeof(short));
        writer.Write((short)(channels * sizeof(short)));
        writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataLength);
        foreach (var sample in samples)
        {
            writer.Write(sample);
        }

        return path;
    }
}
