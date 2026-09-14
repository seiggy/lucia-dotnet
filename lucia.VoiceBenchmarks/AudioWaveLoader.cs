using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace lucia.VoiceBenchmarks;

public static class AudioWaveLoader
{
    public static float[] LoadMono16KhzFloatSamples(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Audio clip not found: {fullPath}", fullPath);
        }

        using var reader = new AudioFileReader(fullPath);
        if (reader.WaveFormat.Encoding != WaveFormatEncoding.Pcm &&
            reader.WaveFormat.Encoding != WaveFormatEncoding.IeeeFloat &&
            reader.WaveFormat.Encoding != WaveFormatEncoding.Extensible)
        {
            throw new NotSupportedException(
                $"Unsupported WAV encoding '{reader.WaveFormat.Encoding}' in '{fullPath}'. Only PCM and IEEE float WAVs are supported.");
        }

        ISampleProvider sampleProvider = reader.ToSampleProvider();
        if (sampleProvider.WaveFormat.Channels > 2)
        {
            throw new NotSupportedException(
                $"Unsupported channel count '{sampleProvider.WaveFormat.Channels}' in '{fullPath}'. Only mono and stereo WAVs are supported.");
        }
        if (sampleProvider.WaveFormat.Channels == 2)
        {
            sampleProvider = new StereoToMonoSampleProvider(sampleProvider);
        }

        if (sampleProvider.WaveFormat.SampleRate != 16000)
        {
            sampleProvider = new WdlResamplingSampleProvider(sampleProvider, 16000);
        }

        var buffer = new float[4096];
        var samples = new List<float>();
        int read;
        while ((read = sampleProvider.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (var index = 0; index < read; index++)
            {
                samples.Add(buffer[index]);
            }
        }

        return samples.ToArray();
    }
}
