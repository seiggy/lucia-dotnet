namespace lucia.Wyoming.Diarization;

public sealed record SpeakerEmbedding
{
    public required float[] Vector { get; init; }
    public TimeSpan Duration { get; init; }
    public DateTimeOffset ExtractedAt { get; init; } = DateTimeOffset.UtcNow;

    public float CosineSimilarity(SpeakerEmbedding other)
    {
        if (Vector.Length != other.Vector.Length)
        {
            throw new ArgumentException("Embedding dimensions must match");
        }

        float dot = 0;
        float normA = 0;
        float normB = 0;

        for (var i = 0; i < Vector.Length; i++)
        {
            dot += Vector[i] * other.Vector[i];
            normA += Vector[i] * Vector[i];
            normB += other.Vector[i] * other.Vector[i];
        }

        var denominator = MathF.Sqrt(normA) * MathF.Sqrt(normB);
        return denominator == 0 ? 0 : dot / denominator;
    }
}
