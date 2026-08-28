using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace lucia.Wyoming.Diarization;

public sealed partial class SpeakerProfileDeletionService(
    ISpeakerProfileStore profileStore,
    AudioClipService clipService,
    ILogger<SpeakerProfileDeletionService> logger) : BackgroundService
{
    private static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(1);
    private readonly ConcurrentDictionary<string, byte> _pendingPurges = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Lazy<Task>> _activeDeletions = new(StringComparer.Ordinal);

    public async Task DeleteAsync(string profileId, CancellationToken ct)
    {
        var operation = _activeDeletions.GetOrAdd(
            profileId,
            static (id, service) => new Lazy<Task>(
                () => service.DeleteCoreAsync(id),
                LazyThreadSafetyMode.ExecutionAndPublication),
            this);
        var deletionTask = operation.Value;
        _ = deletionTask.ContinueWith(
            static (_, state) =>
            {
                var (service, id, activeOperation) =
                    ((SpeakerProfileDeletionService, string, Lazy<Task>))state!;
                service._activeDeletions.TryRemove(
                    new KeyValuePair<string, Lazy<Task>>(id, activeOperation));
            },
            (this, profileId, operation),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        await deletionTask.WaitAsync(ct).ConfigureAwait(false);
    }

    private async Task DeleteCoreAsync(string profileId)
    {
        await clipService.ProfileLifecycleLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        var deletionCommitted = false;
        var tombstoneCreated = false;
        try
        {
            tombstoneCreated = clipService.TombstoneProfileClips(profileId);
            if (await profileStore.GetAsync(profileId, CancellationToken.None).ConfigureAwait(false) is null)
            {
                deletionCommitted = true;
                TryPurge(profileId);
                return;
            }

            await profileStore.DeleteAsync(profileId, CancellationToken.None).ConfigureAwait(false);
            deletionCommitted = true;
            TryPurge(profileId);
        }
        finally
        {
            if (!deletionCommitted && tombstoneCreated)
            {
                clipService.AllowProfileClips(profileId);
                clipService.RemoveProfileClipTombstone(profileId);
            }

            clipService.ProfileLifecycleLock.Release();
        }
    }

    public async Task<bool> DeleteExpiredProvisionalAsync(
        string profileId,
        DateTimeOffset cutoff,
        CancellationToken ct)
    {
        if (profileStore is not IConditionalSpeakerProfileStore conditionalStore)
        {
            return false;
        }

        await clipService.ProfileLifecycleLock.WaitAsync(ct).ConfigureAwait(false);
        var deletionCommitted = false;
        var tombstoneCreated = false;
        try
        {
            tombstoneCreated = clipService.TombstoneProfileClips(profileId);
            var deleted = await conditionalStore.DeleteExpiredProvisionalAsync(profileId, cutoff, ct)
                .ConfigureAwait(false);

            if (!deleted)
            {
                return false;
            }

            deletionCommitted = true;
            TryPurge(profileId);
            return true;
        }
        finally
        {
            if (!deletionCommitted && tombstoneCreated)
            {
                clipService.AllowProfileClips(profileId);
                clipService.RemoveProfileClipTombstone(profileId);
            }

            clipService.ProfileLifecycleLock.Release();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await TryReconcileOrphanedClipsAsync(stoppingToken).ConfigureAwait(false);
        using var timer = new PeriodicTimer(RetryInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await TryReconcileOrphanedClipsAsync(stoppingToken).ConfigureAwait(false);
            foreach (var profileId in _pendingPurges.Keys)
            {
                TryPurge(profileId);
            }
        }

    }

    private async Task TryReconcileOrphanedClipsAsync(CancellationToken ct)
    {
        try
        {
            await ReconcileOrphanedClipsAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogReconciliationDeferred(logger, ex);
        }
    }

    internal async Task ReconcileOrphanedClipsAsync(CancellationToken ct)
    {
        var storedProfileIds = clipService.GetStoredProfileIds();
        foreach (var profileId in storedProfileIds)
        {
            try
            {
                clipService.RemoveIncompleteProfileClips(profileId);
            }
            catch (ArgumentException ex)
            {
                LogLegacyDirectorySkipped(logger, profileId, ex);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                LogIncompleteClipCleanupDeferred(logger, ex);
            }
        }

        foreach (var profileId in storedProfileIds)
        {
            if (_pendingPurges.ContainsKey(profileId))
            {
                TryPurge(profileId);
                continue;
            }

            if (await profileStore.GetAsync(profileId, ct).ConfigureAwait(false) is not null)
            {
                continue;
            }

            try
            {
                clipService.BlockProfileClips(profileId);
                TryPurge(profileId);
            }
            catch (ArgumentException ex)
            {
                LogLegacyDirectorySkipped(logger, profileId, ex);
            }
            catch (InvalidOperationException ex)
            {
                LogReconciliationBusy(logger, profileId, ex);
                continue;
            }
        }
    }

    private void TryPurge(string profileId)
    {
        try
        {
            clipService.PurgeProfileClips(profileId);
            _pendingPurges.TryRemove(profileId, out _);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _pendingPurges.TryAdd(profileId, 0);
            LogPurgeDeferred(logger, profileId, ex);
        }
    }

    [LoggerMessage(
        EventId = 7101,
        Level = LogLevel.Warning,
        Message = "Deferring audio clip purge for deleted speaker profile {ProfileId}")]
    private static partial void LogPurgeDeferred(
        ILogger logger,
        string profileId,
        Exception exception);

    [LoggerMessage(
        EventId = 7102,
        Level = LogLevel.Warning,
        Message = "Skipping noncanonical legacy voice clip directory {ProfileId}")]
    private static partial void LogLegacyDirectorySkipped(
        ILogger logger,
        string profileId,
        Exception exception);

    [LoggerMessage(
        EventId = 7103,
        Level = LogLevel.Warning,
        Message = "Deferring reconciliation of orphaned speaker recordings")]
    private static partial void LogReconciliationDeferred(
        ILogger logger,
        Exception exception);

    [LoggerMessage(
        EventId = 7104,
        Level = LogLevel.Debug,
        Message = "Deferring reconciliation for busy speaker profile {ProfileId}")]
    private static partial void LogReconciliationBusy(
        ILogger logger,
        string profileId,
        Exception exception);

    [LoggerMessage(
        EventId = 7105,
        Level = LogLevel.Warning,
        Message = "Deferring cleanup of incomplete speaker recordings")]
    private static partial void LogIncompleteClipCleanupDeferred(
        ILogger logger,
        Exception exception);
}
