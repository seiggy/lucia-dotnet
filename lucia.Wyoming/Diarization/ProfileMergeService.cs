using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace lucia.Wyoming.Diarization;

/// <summary>
/// Merges two speaker profiles by combining embeddings, moving audio clips,
/// and deleting the source profile.
/// </summary>
public sealed class ProfileMergeService(
    ISpeakerProfileStore profileStore,
    AudioClipService clipService,
    ILogger<ProfileMergeService> logger) : BackgroundService
{
    private static readonly TimeSpan RecoveryInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await TryRecoverPendingMergesAsync(stoppingToken).ConfigureAwait(false);
        using var timer = new PeriodicTimer(RecoveryInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await TryRecoverPendingMergesAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    internal async Task RecoverPendingMergesAsync(CancellationToken stoppingToken)
    {
        var profiles = await profileStore.GetAllAsync(stoppingToken).ConfigureAwait(false);
        var pendingMerges = profiles
            .Where(static profile => profile.MergeTargetProfileId is not null)
            .Select(static profile => (SourceId: profile.Id, TargetId: profile.MergeTargetProfileId!))
            .Concat(profiles.SelectMany(
                static target => target.PendingMergeSourceIds.Select(
                    sourceId => (SourceId: sourceId, TargetId: target.Id))))
            .Distinct()
            .OrderBy(static merge => merge.SourceId, StringComparer.Ordinal)
            .ThenBy(static merge => merge.TargetId, StringComparer.Ordinal);
        foreach (var pendingMerge in pendingMerges)
        {
            try
            {
                await MergeAsync(
                    pendingMerge.SourceId,
                    pendingMerge.TargetId,
                    stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (KeyNotFoundException)
            {
                var source = await profileStore.GetAsync(
                    pendingMerge.SourceId,
                    stoppingToken).ConfigureAwait(false);
                if (source is not null
                    && string.Equals(
                        source.MergeTargetProfileId,
                        pendingMerge.TargetId,
                        StringComparison.Ordinal))
                {
                    await profileStore.UpdateAtomicAsync(
                        source.Id,
                        static source => source with { MergeTargetProfileId = null },
                        stoppingToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Deferred recovery of an interrupted speaker profile merge");
            }
        }

    }

    private async Task TryRecoverPendingMergesAsync(CancellationToken stoppingToken)
    {
        try
        {
            await RecoverPendingMergesAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Deferred speaker profile merge recovery");
        }
    }

    public async Task<SpeakerProfile> MergeAsync(
        string sourceProfileId,
        string targetProfileId,
        CancellationToken ct = default)
    {
        await clipService.ProfileLifecycleLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await MergeCoreAsync(sourceProfileId, targetProfileId, ct).ConfigureAwait(false);
        }
        finally
        {
            clipService.ProfileLifecycleLock.Release();
        }
    }

    private async Task<SpeakerProfile> MergeCoreAsync(
        string sourceProfileId,
        string targetProfileId,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceProfileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetProfileId);

        if (string.Equals(sourceProfileId, targetProfileId, StringComparison.Ordinal))
        {
            throw new ArgumentException("Cannot merge a profile into itself.");
        }

        var target = await profileStore.GetAsync(targetProfileId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Target profile '{targetProfileId}' not found.");
        if (target.MergeTargetProfileId is not null)
        {
            throw new ProfileMergeConflictException("A profile being merged cannot receive another profile.");
        }
        if (target.MergedProfileIds.Contains(sourceProfileId, StringComparer.Ordinal))
        {
            return target;
        }
        if (target.PendingMergeSourceIds.Contains(sourceProfileId, StringComparer.Ordinal))
        {
            return await CompletePendingMergeAsync(sourceProfileId, targetProfileId).ConfigureAwait(false);
        }

        var source = await profileStore.GetAsync(sourceProfileId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Source profile '{sourceProfileId}' not found.");
        if (source.PendingMergeSourceIds.Length > 0)
        {
            throw new ProfileMergeConflictException("A profile with pending incoming merges cannot be merged.");
        }

        if (source.MergeTargetProfileId is null)
        {
            source = await profileStore.UpdateAtomicAsync(
                sourceProfileId,
                source =>
                {
                    EnsureCompatibleEmbeddingDimensions(source, target);
                    return source with { MergeTargetProfileId = targetProfileId };
                },
                ct).ConfigureAwait(false)
                ?? throw new KeyNotFoundException($"Source profile '{sourceProfileId}' not found.");
        }
        else if (!string.Equals(source.MergeTargetProfileId, targetProfileId, StringComparison.Ordinal))
        {
            throw new ProfileMergeConflictException("The source profile is already merging into another profile.");
        }

        var merged = await profileStore.UpdateAtomicAsync(
            targetProfileId,
            target =>
            {
                if (target.MergedProfileIds.Contains(sourceProfileId, StringComparer.Ordinal))
                {
                    return target;
                }
                if (target.PendingMergeSourceIds.Contains(sourceProfileId, StringComparer.Ordinal))
                {
                    return target;
                }

                EnsureCompatibleEmbeddingDimensions(source, target);
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
                    PendingMergeSourceIds = [.. target.PendingMergeSourceIds, sourceProfileId],
                };
            },
            ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Target profile '{targetProfileId}' not found.");

        return await CompletePendingMergeAsync(sourceProfileId, targetProfileId).ConfigureAwait(false);
    }

    private async Task<SpeakerProfile> CompletePendingMergeAsync(
        string sourceProfileId,
        string targetProfileId)
    {
        var tombstoneCreated = clipService.TombstoneProfileClips(sourceProfileId);
        var sourceDeletionCommitted = false;
        try
        {
            await clipService.MoveTombstonedProfileClipsAsync(
                sourceProfileId,
                targetProfileId).ConfigureAwait(false);
            await profileStore.DeleteAsync(sourceProfileId, CancellationToken.None).ConfigureAwait(false);
            sourceDeletionCommitted = true;
            clipService.CompleteProfileClipTombstone(sourceProfileId);
        }
        finally
        {
            if (!sourceDeletionCommitted && tombstoneCreated)
            {
                clipService.AllowProfileClips(sourceProfileId);
                clipService.RemoveProfileClipTombstone(sourceProfileId);
            }
        }

        var merged = await profileStore.UpdateAtomicAsync(
            targetProfileId,
            target => target with
            {
                PendingMergeSourceIds =
                [
                    .. target.PendingMergeSourceIds.Where(
                        id => !string.Equals(id, sourceProfileId, StringComparison.Ordinal)),
                ],
                MergedProfileIds = target.MergedProfileIds.Contains(sourceProfileId, StringComparer.Ordinal)
                    ? target.MergedProfileIds
                    : [.. target.MergedProfileIds, sourceProfileId],
            },
            CancellationToken.None).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The merge target disappeared after its profile update committed.");

        logger.LogInformation(
            "Merged speaker profiles ({EmbeddingCount} total embeddings)",
            merged.Embeddings.Length);
        return merged;
    }

    private static void EnsureCompatibleEmbeddingDimensions(SpeakerProfile source, SpeakerProfile target)
    {
        if (source.Embeddings.Length == 0
            || target.Embeddings.Length == 0
            || source.AverageEmbedding.Length == 0
            || target.AverageEmbedding.Length == 0
            || source.Embeddings.Any(static embedding => embedding.Length == 0)
            || target.Embeddings.Any(static embedding => embedding.Length == 0))
        {
            throw new ProfileMergeConflictException("Cannot merge speaker profiles containing empty embeddings.");
        }

        var dimensions = source.Embeddings
            .Concat(target.Embeddings)
            .Append(source.AverageEmbedding)
            .Append(target.AverageEmbedding)
            .Where(static embedding => embedding.Length > 0)
            .Select(static embedding => embedding.Length)
            .Distinct()
            .Take(2)
            .Count();
        if (dimensions > 1)
        {
            throw new ProfileMergeConflictException("Cannot merge speaker profiles with different embedding dimensions.");
        }
    }
}
