namespace lucia.Wyoming.Diarization;

public sealed record OnboardingStepResult
{
    public required OnboardingStepStatus Status { get; init; }
    public required string Message { get; init; }
    public string? NextPrompt { get; init; }
    public SpeakerProfile? CompletedProfile { get; init; }
    public int ProgressPercent { get; init; }

    public static OnboardingStepResult CreateNextPrompt(string prompt, int progress) => new()
    {
        Status = OnboardingStepStatus.NextPrompt,
        Message = prompt,
        NextPrompt = prompt,
        ProgressPercent = progress,
    };

    public static OnboardingStepResult Retry(string reason) => new()
    {
        Status = OnboardingStepStatus.Retry,
        Message = reason,
    };

    public static OnboardingStepResult Complete(string message, SpeakerProfile profile) => new()
    {
        Status = OnboardingStepStatus.Complete,
        Message = message,
        CompletedProfile = profile,
        ProgressPercent = 100,
    };
}
