using System.Text.Json.Serialization;

namespace lucia.Wyoming.Diarization;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OnboardingStepStatus
{
    NextPrompt,
    Retry,
    Complete,
    Error,
}
