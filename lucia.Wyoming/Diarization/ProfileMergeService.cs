using Microsoft.Extensions.Logging;

namespace lucia.Wyoming.Diarization;

/// <summary>
/// Merges two speaker profiles by combining embeddings, moving audio clips,
/// and deleting the source profile.
/// </summary>
public sealed class ProfileMergeService(
    ISpeakerProfileStore profileStore,
    AudioClipService clipService,
    SpeakerProfileDeletionService deletionService,
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
        var target = await profileStore.GetAsync(targetProfileId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Target profile '{targetProfileId}' not found.");

        var alreadyMerged = target.MergedProfileIds.Contains(sourceProfileId, StringComparer.Ordinal);
        var combinedEmbeddings = new List<float[]>(target.Embeddings ?? []);
        if (!alreadyMerged && source.Embeddings is not null)
        {
            combinedEmbeddings.AddRange(source.Embeddings);
        }

        var averageEmbedding = combinedEmbeddings.Count > 0
            ? IDiarizationEngine.ComputeAverageEmbedding(combinedEmbeddings)
            : target.AverageEmbedding;

        var merged = alreadyMerged
            ? target
            : target with
            {
                Embeddings = combinedEmbeddings.ToArray(),
                AverageEmbedding = averageEmbedding,
                InteractionCount = target.InteractionCount + source.InteractionCount,
                UpdatedAt = DateTimeOffset.UtcNow,
                LastSeenAt = source.LastSeenAt > target.LastSeenAt ? source.LastSeenAt : target.LastSeenAt,
                MergedProfileIds = [.. target.MergedProfileIds, sourceProfileId],
            };

        clipService.BlockProfileClips(sourceProfileId);
        var targetBlocked = false;
        try
        {
            clipService.BlockProfileClips(targetProfileId);
            targetBlocked = true;
            await profileStore.UpdateAsync(merged, ct).ConfigureAwait(false);
            await clipService.MoveBlockedProfileClipsAsync(sourceProfileId, targetProfileId, ct).ConfigureAwait(false);
            await deletionService.DeleteBlockedAsync(sourceProfileId, ct).ConfigureAwait(false);
            clipService.AllowProfileClips(targetProfileId);
        }
        catch
        {
            clipService.AllowProfileClips(sourceProfileId);
            if (targetBlocked)
            {
                clipService.AllowProfileClips(targetProfileId);
            }
            throw;
        }

        logger.LogInformation(
            "Merged speaker profile {SourceId} into {TargetId} ({EmbeddingCount} total embeddings)",
            sourceProfileId, targetProfileId, combinedEmbeddings.Count);

        return merged;
    }
}
