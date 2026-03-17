namespace lucia.Wyoming.Diarization;

public sealed record SpeakerIdentification
{
    public required string ProfileId { get; init; }
    public required string Name { get; init; }
    public required float Similarity { get; init; }
    public required bool IsAuthorized { get; init; }
}
