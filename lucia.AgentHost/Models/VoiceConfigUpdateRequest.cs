namespace lucia.AgentHost.Models;

public sealed record VoiceConfigUpdateRequest
{
    public bool? IgnoreUnknownVoices { get; init; }
    public bool? AutoCreateProvisionalProfiles { get; init; }
    public int? MaxAutoProfiles { get; init; }
    public float? SpeakerVerificationThreshold { get; init; }
    public float? ProvisionalMatchThreshold { get; init; }
    public bool? AdaptiveProfiles { get; init; }
    public int? ProvisionalRetentionDays { get; init; }
    public int? SuggestEnrollmentAfter { get; init; }
}
