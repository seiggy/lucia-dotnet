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

        if (speaker is null)
        {
            logger.LogDebug("Ignoring command from unknown speaker (ignore_unknown_voices=true)");
            return false;
        }

        return true;
    }
}
