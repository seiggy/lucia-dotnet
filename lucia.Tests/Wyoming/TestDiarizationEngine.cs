using lucia.Wyoming.Diarization;

namespace lucia.Tests.Wyoming;

public sealed class TestDiarizationEngine : IDiarizationEngine
{
    private readonly float[] _testEmbedding;

    public TestDiarizationEngine(
        SpeakerIdentification? identification = null,
        float[]? embeddingVector = null)
    {
        Identification = identification;
        _testEmbedding = embeddingVector
            ?? Enumerable.Range(0, 128)
                .Select(static i => (float)i / 128)
                .ToArray();
    }

    public bool IsReady => true;

    public SpeakerIdentification? Identification { get; set; }

    public bool ShouldThrow { get; set; }

    public int ExtractEmbeddingCallCount { get; private set; }

    public int IdentifySpeakerCallCount { get; private set; }

    public int LastSampleRate { get; private set; }

    public float[] LastAudioSamples { get; private set; } = [];

    public SpeakerEmbedding ExtractEmbedding(ReadOnlySpan<float> audioSamples, int sampleRate)
    {
        if (ShouldThrow)
        {
            throw new InvalidOperationException("Test failure");
        }

        ExtractEmbeddingCallCount++;
        LastSampleRate = sampleRate;
        LastAudioSamples = [.. audioSamples];

        return new SpeakerEmbedding
        {
            Vector = _testEmbedding,
            Duration = TimeSpan.FromSeconds((double)audioSamples.Length / sampleRate),
        };
    }

    public SpeakerIdentification? IdentifySpeaker(
        SpeakerEmbedding embedding,
        IReadOnlyList<SpeakerProfile> enrolledProfiles,
        float threshold = 0.7f)
    {
        IdentifySpeakerCallCount++;
        return Identification;
    }

    public void Dispose()
    {
    }
}
