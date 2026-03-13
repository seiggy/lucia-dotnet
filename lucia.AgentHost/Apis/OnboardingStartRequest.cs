namespace lucia.AgentHost.Apis;

public sealed record OnboardingStartRequest
{
    public required string SpeakerName { get; init; }

    public string? ProvisionalProfileId { get; init; }

    public string? WakeWordPhrase { get; init; }
}
