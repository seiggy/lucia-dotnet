namespace lucia.Wyoming.Diarization;

public interface IDiarizationEngine
{
    bool IsReady { get; }

    SpeakerEmbedding ExtractEmbedding(ReadOnlySpan<float> audioSamples, int sampleRate);

    SpeakerIdentification? IdentifySpeaker(
        SpeakerEmbedding embedding,
        IReadOnlyList<SpeakerProfile> enrolledProfiles,
        float threshold = 0.7f);

    /// <summary>Compute average embedding from multiple samples.</summary>
    static float[] ComputeAverageEmbedding(IReadOnlyList<float[]> embeddings)
    {
        if (embeddings.Count == 0)
        {
            throw new ArgumentException("At least one embedding required");
        }

        var dim = embeddings[0].Length;
        var avg = new float[dim];

        foreach (var emb in embeddings)
        {
            for (var i = 0; i < dim; i++)
            {
                avg[i] += emb[i];
            }
        }

        for (var i = 0; i < dim; i++)
        {
            avg[i] /= embeddings.Count;
        }

        return avg;
    }
}
