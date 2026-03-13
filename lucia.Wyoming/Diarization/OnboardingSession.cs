namespace lucia.Wyoming.Diarization;

public sealed record OnboardingSession
{
    public required string Id { get; init; }
    public required string SpeakerName { get; init; }
    public string? ProvisionalProfileId { get; init; }
    public required IReadOnlyList<string> Prompts { get; init; }
    public List<float[]> CollectedEmbeddings { get; init; } = [];
    public int CurrentPromptIndex { get; set; }
    public OnboardingStatus Status { get; set; } = OnboardingStatus.InProgress;
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
}
