using lucia.Wyoming.Diarization;

namespace lucia.AgentHost.Conversation;

internal sealed class VoiceOnboardingConversation
{
    public required string DeviceId { get; init; }
    public required DateTimeOffset LastActivityAt { get; set; }
    public VoiceOnboardingStage Stage { get; set; }
    public string? Name { get; set; }
    public string? Room { get; set; }
    public string? Preferences { get; set; }
    public OnboardingSession? Enrollment { get; set; }
}
