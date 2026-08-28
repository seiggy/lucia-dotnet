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

    public async Task DeleteAsync(string profileId, CancellationToken ct)
    {
        clipService.BlockProfileClips(profileId);
        try
        {
            await profileStore.DeleteAsync(profileId, ct).ConfigureAwait(false);
        }
        catch
        {
            clipService.AllowProfileClips(profileId);
            throw;
        }

        TryPurge(profileId);
    }

    public async Task DeleteBlockedAsync(string profileId, CancellationToken ct)
    {
        await profileStore.DeleteAsync(profileId, ct).ConfigureAwait(false);
        TryPurge(profileId);
    }

    public async Task<bool> DeleteExpiredProvisionalAsync(
        string profileId,
        DateTimeOffset cutoff,
        CancellationToken ct)
    {
        clipService.BlockProfileClips(profileId);
        bool deleted;
        try
        {
            deleted = await profileStore.DeleteExpiredProvisionalAsync(profileId, cutoff, ct)
                .ConfigureAwait(false);
        }
        catch
        {
            clipService.AllowProfileClips(profileId);
            throw;
        }

        if (!deleted)
        {
            clipService.AllowProfileClips(profileId);
            return false;
        }

        TryPurge(profileId);
        return true;
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

    private async Task ReconcileOrphanedClipsAsync(CancellationToken ct)
    {
        foreach (var profileId in clipService.GetStoredProfileIds())
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
}
