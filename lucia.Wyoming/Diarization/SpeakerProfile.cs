namespace lucia.Wyoming.Diarization;

public sealed record SpeakerProfile
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public bool IsProvisional { get; init; }
    public bool IsAuthorized { get; init; } = true;
    public string[]? AllowedSkills { get; init; }
    public float[][] Embeddings { get; init; } = [];
    public float[] AverageEmbedding { get; init; } = [];
    public int InteractionCount { get; init; }
    public DateTimeOffset EnrolledAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastSeenAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExpiresAt { get; init; }
}
