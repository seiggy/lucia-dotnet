namespace lucia.Wyoming.Diarization;

public sealed class VoiceProfileOptions
{
    public const string SectionName = "Wyoming:VoiceProfiles";
    private const float MinSpeakerVerificationThreshold = 0.10f;
    private const float MaxSpeakerVerificationThreshold = 0.95f;
    private const float MinProvisionalMatchThreshold = 0.10f;
    private const float MaxProvisionalMatchThreshold = 0.95f;

    public bool IgnoreUnknownVoices { get; set; }
    public float SpeakerVerificationThreshold { get; set; } = 0.35f;
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
    public int MaxClipsPerProfile { get; set; } = 5;
    public int MaxAutoProfiles { get; set; } = 10;

    public static bool IsValidSpeakerVerificationThreshold(float value) =>
        float.IsFinite(value)
        && value >= MinSpeakerVerificationThreshold
        && value <= MaxSpeakerVerificationThreshold;

    public static bool IsValidProvisionalMatchThreshold(float value) =>
        float.IsFinite(value)
        && value >= MinProvisionalMatchThreshold
        && value <= MaxProvisionalMatchThreshold;
}
