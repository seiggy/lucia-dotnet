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
    private readonly SemaphoreSlim _trackLock = new(1, 1);

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
    /// Returns the provisional profile and whether enrollment should be suggested,
    /// or <c>null</c> when provisional tracking is disabled or the auto-profile cap is reached.
    /// </summary>
    public async Task<(SpeakerProfile Profile, bool ShouldSuggestEnrollment)?> TrackUnknownSpeakerAsync(
        SpeakerEmbedding embedding,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(embedding);

        if (!_options.AutoCreateProvisionalProfiles)
        {
            _logger.LogDebug("Provisional profile creation is disabled — skipping unknown speaker tracking");
            return null;
        }

        await _trackLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var provisionals = await _profileStore.GetProvisionalProfilesAsync(ct).ConfigureAwait(false);

            // Always attempt to match against existing provisional profiles,
            // even when the cap is reached — returning speakers should still be tracked.
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

                var updatedProfile = await _profileStore.UpdateAtomicAsync(
                    profile.Id,
                    existingProfile =>
                    {
                        var now = DateTimeOffset.UtcNow;
                        var updatedEmbeddings = existingProfile.Embeddings
                            .Append(embedding.Vector.ToArray())
                            .ToArray();
                        var averageEmbedding = IDiarizationEngine.ComputeAverageEmbedding(updatedEmbeddings);

                        return existingProfile with
                        {
                            Embeddings = updatedEmbeddings,
                            AverageEmbedding = averageEmbedding,
                            InteractionCount = existingProfile.InteractionCount + 1,
                            LastSeenAt = now,
                            UpdatedAt = now,
                        };
                    },
                    ct).ConfigureAwait(false);

                if (updatedProfile is null)
                {
                    throw new InvalidOperationException($"Provisional profile '{profile.Id}' was not found.");
                }

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

            // No existing profile matched — check the cap before creating a new one.
            if (provisionals.Count >= _options.MaxAutoProfiles)
            {
                _logger.LogDebug(
                    "Auto-profile cap reached ({Count}/{Max}) — not creating provisional profile for new speaker",
                    provisionals.Count,
                    _options.MaxAutoProfiles);
                return null;
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
        finally
        {
            _trackLock.Release();
        }
    }
}
