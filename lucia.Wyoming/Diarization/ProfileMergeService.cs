using Microsoft.Extensions.Logging;

namespace lucia.Wyoming.Diarization;

/// <summary>
/// Merges two speaker profiles by combining embeddings, moving audio clips,
/// and deleting the source profile.
/// </summary>
public sealed class ProfileMergeService(
    ISpeakerProfileStore profileStore,
    AudioClipService clipService,
    ILogger<ProfileMergeService> logger)
{
    public async Task<SpeakerProfile> MergeAsync(
        string sourceProfileId,
        string targetProfileId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceProfileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetProfileId);

        if (string.Equals(sourceProfileId, targetProfileId, StringComparison.Ordinal))
        {
            throw new ArgumentException("Cannot merge a profile into itself.");
        }

        var source = await profileStore.GetAsync(sourceProfileId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Source profile '{sourceProfileId}' not found.");
        _ = await profileStore.GetAsync(targetProfileId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Target profile '{targetProfileId}' not found.");

        // Move audio clips from source to target
        await clipService.MoveClipsAsync(sourceProfileId, targetProfileId, ct).ConfigureAwait(false);

        var merged = await profileStore.UpdateAtomicAsync(
            targetProfileId,
            target =>
            {
                var combinedEmbeddings = target.Embeddings.Concat(source.Embeddings).ToArray();
                return target with
                {
                    Embeddings = combinedEmbeddings,
                    AverageEmbedding = combinedEmbeddings.Length > 0
                        ? IDiarizationEngine.ComputeAverageEmbedding(combinedEmbeddings)
                        : target.AverageEmbedding,
                    InteractionCount = target.InteractionCount + source.InteractionCount,
                    UpdatedAt = DateTimeOffset.UtcNow,
                    LastSeenAt = source.LastSeenAt > target.LastSeenAt ? source.LastSeenAt : target.LastSeenAt,
                };
            },
            ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Target profile '{targetProfileId}' not found.");

        await profileStore.DeleteAsync(sourceProfileId, ct).ConfigureAwait(false);

        logger.LogInformation(
            "Merged speaker profile {SourceId} into {TargetId} ({EmbeddingCount} total embeddings)",
            sourceProfileId, targetProfileId, merged.Embeddings.Length);

        return merged;
    }
}
