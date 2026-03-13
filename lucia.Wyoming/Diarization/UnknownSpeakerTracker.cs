using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucia.Wyoming.Diarization;

/// <summary>
/// Automatically tracks unknown speakers by creating provisional profiles.
/// Suggests enrollment after repeated interactions.
/// </summary>
public sealed class UnknownSpeakerTracker
{
    private readonly ISpeakerProfileStore _profileStore;
    private readonly VoiceProfileOptions _options;
    private readonly ILogger<UnknownSpeakerTracker> _logger;

    public UnknownSpeakerTracker(
        ISpeakerProfileStore profileStore,
        IOptions<VoiceProfileOptions> options,
        ILogger<UnknownSpeakerTracker> logger)
    {
        ArgumentNullException.ThrowIfNull(profileStore);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _profileStore = profileStore;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Track an unrecognized speaker. Creates or updates a provisional profile.
    /// Returns the provisional profile and whether enrollment should be suggested.
    /// </summary>
    public async Task<(SpeakerProfile Profile, bool ShouldSuggestEnrollment)> TrackUnknownSpeakerAsync(
        SpeakerEmbedding embedding,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(embedding);

        var provisionals = await _profileStore.GetProvisionalProfilesAsync(ct).ConfigureAwait(false);

        foreach (var profile in provisionals)
        {
            if (profile.AverageEmbedding.Length == 0)
            {
                continue;
            }

            var profileEmbedding = new SpeakerEmbedding { Vector = profile.AverageEmbedding };
            var similarity = embedding.CosineSimilarity(profileEmbedding);

            if (similarity <= _options.ProvisionalMatchThreshold)
            {
                continue;
            }

            var now = DateTimeOffset.UtcNow;
            var updatedEmbeddings = profile.Embeddings.Append(embedding.Vector).ToArray();
            var averageEmbedding = IDiarizationEngine.ComputeAverageEmbedding(updatedEmbeddings);

            var updatedProfile = profile with
            {
                Embeddings = updatedEmbeddings,
                AverageEmbedding = averageEmbedding,
                InteractionCount = profile.InteractionCount + 1,
                LastSeenAt = now,
                UpdatedAt = now,
            };

            await _profileStore.UpdateAsync(updatedProfile, ct).ConfigureAwait(false);

            var shouldSuggestEnrollment =
                updatedProfile.InteractionCount >= _options.SuggestEnrollmentAfter;

            if (shouldSuggestEnrollment)
            {
                _logger.LogInformation(
                    "Provisional speaker {ProfileId} reached {Count} interactions, suggesting enrollment",
                    updatedProfile.Id,
                    updatedProfile.InteractionCount);
            }

            return (updatedProfile, shouldSuggestEnrollment);
        }

        var createdAt = DateTimeOffset.UtcNow;
        var newProfile = new SpeakerProfile
        {
            Id = $"unknown-{Guid.NewGuid():N}",
            Name = $"Unknown Speaker {provisionals.Count + 1}",
            IsProvisional = true,
            IsAuthorized = false,
            Embeddings = [embedding.Vector],
            AverageEmbedding = embedding.Vector,
            InteractionCount = 1,
            EnrolledAt = createdAt,
            UpdatedAt = createdAt,
            LastSeenAt = createdAt,
            ExpiresAt = createdAt.AddDays(_options.ProvisionalRetentionDays),
        };

        await _profileStore.CreateAsync(newProfile, ct).ConfigureAwait(false);
        _logger.LogInformation(
            "Created provisional profile {ProfileId} for new unknown speaker",
            newProfile.Id);

        return (newProfile, false);
    }
}
