using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucia.Wyoming.Diarization;

public sealed class AdaptiveProfileUpdater(
    ISpeakerProfileStore profileStore,
    IOptions<VoiceProfileOptions> options,
    ILogger<AdaptiveProfileUpdater> logger)
{
    public async Task TryUpdateAsync(
        SpeakerIdentification identification,
        SpeakerEmbedding currentEmbedding,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(identification);
        ArgumentNullException.ThrowIfNull(currentEmbedding);

        var opts = options.Value;
        if (!opts.AdaptiveProfiles || identification.Similarity < opts.HighConfidenceThreshold)
        {
            return;
        }

        var profile = await profileStore.GetAsync(identification.ProfileId, ct).ConfigureAwait(false);
        if (profile is null || profile.IsProvisional)
        {
            return;
        }

        if (profile.AverageEmbedding.Length != currentEmbedding.Vector.Length)
        {
            logger.LogWarning(
                "Skipping adaptive update for profile {ProfileId} because embedding dimensions differ ({ExistingLength} != {CurrentLength})",
                identification.ProfileId,
                profile.AverageEmbedding.Length,
                currentEmbedding.Vector.Length);
            return;
        }

        var alpha = opts.AdaptiveAlpha;
        var updated = new float[profile.AverageEmbedding.Length];
        for (var i = 0; i < updated.Length; i++)
        {
            updated[i] = ((1 - alpha) * profile.AverageEmbedding[i]) + (alpha * currentEmbedding.Vector[i]);
        }

        var now = DateTimeOffset.UtcNow;
        var updatedProfile = profile with
        {
            AverageEmbedding = updated,
            UpdatedAt = now,
            LastSeenAt = now,
            InteractionCount = profile.InteractionCount + 1,
        };

        await profileStore.UpdateAsync(updatedProfile, ct).ConfigureAwait(false);
        logger.LogDebug(
            "Adaptively updated profile {ProfileId} (similarity: {Similarity:F2})",
            identification.ProfileId,
            identification.Similarity);
    }
}
