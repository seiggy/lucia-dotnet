namespace lucia.Wyoming.Diarization;

public sealed class VoiceProfileOptions
{
    public const string SectionName = "Wyoming:VoiceProfiles";

    public bool IgnoreUnknownVoices { get; set; }
    public float SpeakerVerificationThreshold { get; set; } = 0.7f;
    public bool AdaptiveProfiles { get; set; } = true;
    public float AdaptiveAlpha { get; set; } = 0.05f;
    public float HighConfidenceThreshold { get; set; } = 0.85f;
    public float ProvisionalMatchThreshold { get; set; } = 0.65f;
    public int ProvisionalRetentionDays { get; set; } = 30;
    public int SuggestEnrollmentAfter { get; set; } = 5;
    public int OnboardingSampleCount { get; set; } = 5;
    public int MinSampleDurationMs { get; set; } = 1500;
    public float MinSampleSnrDb { get; set; } = 10.0f;
    public bool AutoCreateProvisionalProfiles { get; set; } = true;
    public string AudioClipBasePath { get; set; } = "./data/voice-clips";
    public int MaxClipsPerProfile { get; set; } = 3;
    public int MaxAutoProfiles { get; set; } = 10;
}
