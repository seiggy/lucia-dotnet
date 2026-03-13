using Microsoft.Extensions.Options;

namespace lucia.Wyoming.Diarization;

public sealed class AudioQualityAnalyzer(IOptions<VoiceProfileOptions> options)
{
    public AudioQualityReport Analyze(ReadOnlySpan<float> samples, int sampleRate)
    {
        var opts = options.Value;
        var durationMs = (int)(samples.Length * 1000.0 / sampleRate);

        float sumSquares = 0;
        for (var i = 0; i < samples.Length; i++)
        {
            sumSquares += samples[i] * samples[i];
        }

        var rms = MathF.Sqrt(sumSquares / Math.Max(1, samples.Length));

        return new AudioQualityReport
        {
            DurationMs = durationMs,
            RmsEnergy = rms,
            IsTooQuiet = rms < 0.01f,
            IsTooShort = durationMs < opts.MinSampleDurationMs,
        };
    }
}
