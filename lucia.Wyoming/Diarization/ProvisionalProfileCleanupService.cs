using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucia.Wyoming.Diarization;

/// <summary>
/// Background service that periodically removes expired provisional speaker profiles.
/// </summary>
public sealed class ProvisionalProfileCleanupService : BackgroundService
{
    private readonly ISpeakerProfileStore _profileStore;
    private readonly VoiceProfileOptions _options;
    private readonly ILogger<ProvisionalProfileCleanupService> _logger;

    public ProvisionalProfileCleanupService(
        ISpeakerProfileStore profileStore,
        IOptions<VoiceProfileOptions> options,
        ILogger<ProvisionalProfileCleanupService> logger)
    {
        ArgumentNullException.ThrowIfNull(profileStore);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _profileStore = profileStore;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromHours(24), stoppingToken).ConfigureAwait(false);

                var expiredProfiles = await _profileStore
                    .GetExpiredProvisionalProfilesAsync(_options.ProvisionalRetentionDays, stoppingToken)
                    .ConfigureAwait(false);

                foreach (var profile in expiredProfiles)
                {
                    await _profileStore.DeleteAsync(profile.Id, stoppingToken).ConfigureAwait(false);
                    _logger.LogInformation(
                        "Removed expired provisional profile {ProfileId} ({Name})",
                        profile.Id,
                        profile.Name);
                }

                if (expiredProfiles.Count > 0)
                {
                    _logger.LogInformation(
                        "Cleaned up {Count} expired provisional profiles",
                        expiredProfiles.Count);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during provisional profile cleanup");
            }
        }
    }
}
