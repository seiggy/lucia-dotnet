namespace lucia.Wyoming.Wyoming;

public sealed record SpeakerDetectedEvent : SessionEvent
{
    public required string ProfileId { get; init; }
    public required string ProfileName { get; init; }
    public required float Similarity { get; init; }
    public required bool IsProvisional { get; init; }
}
