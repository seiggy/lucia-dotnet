using System.Text.Json.Serialization;

namespace lucia.Wyoming.Diarization;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OnboardingStatus
{
    InProgress,
    AwaitingSample,
    Processing,
    Complete,
    Cancelled,
    Failed,
}
