using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucia.Wyoming.Diarization;

public sealed class SpeakerVerificationFilter(
    IOptions<VoiceProfileOptions> options,
    ILogger<SpeakerVerificationFilter> logger)
{
    public bool ShouldProcessCommand(SpeakerIdentification? speaker)
    {
        if (!options.Value.IgnoreUnknownVoices)
        {
            return true;
        }

        if (speaker is null || !speaker.IsAuthorized)
        {
            logger.LogDebug(
                "Ignoring command from {SpeakerType} speaker (ignore_unknown_voices=true)",
                speaker is null ? "unknown" : "unauthorized");
            return false;
        }

        return true;
    }
}
