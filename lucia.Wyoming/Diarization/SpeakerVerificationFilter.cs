using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucia.Wyoming.Diarization;

public sealed class SpeakerVerificationFilter(
    IOptionsMonitor<VoiceProfileOptions> options,
    ILogger<SpeakerVerificationFilter> logger)
{
    public bool ShouldProcessCommand(SpeakerIdentification? speaker)
    {
        var current = options.CurrentValue;
        if (!current.IgnoreUnknownVoices)
        {
            return true;
        }

        if (speaker is null || !speaker.IsAuthorized
            || !float.IsFinite(speaker.Similarity)
            || speaker.Similarity < current.SpeakerVerificationThreshold)
        {
            logger.LogDebug(
                "Ignoring command from {SpeakerType} speaker (ignore_unknown_voices=true)",
                speaker is null ? "unknown" : "unauthorized");
            return false;
        }

        return true;
    }
}
