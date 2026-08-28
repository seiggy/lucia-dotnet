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
    private readonly SpeakerProfileDeletionService _deletionService;
    private readonly VoiceProfileOptions _options;
    private readonly ILogger<ProvisionalProfileCleanupService> _logger;

    public ProvisionalProfileCleanupService(
        ISpeakerProfileStore profileStore,
        SpeakerProfileDeletionService deletionService,
        IOptions<VoiceProfileOptions> options,
        ILogger<ProvisionalProfileCleanupService> logger)
    {
        ArgumentNullException.ThrowIfNull(profileStore);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _profileStore = profileStore;
        _deletionService = deletionService;
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
                var cutoff = DateTimeOffset.UtcNow.AddDays(-_options.ProvisionalRetentionDays);
                var deletedCount = 0;

                foreach (var profile in expiredProfiles)
                {
                    if (!await _deletionService
                            .DeleteExpiredProvisionalAsync(profile.Id, cutoff, stoppingToken)
                            .ConfigureAwait(false))
                    {
                        continue;
                    }

                    _logger.LogInformation(
                        "Removed expired provisional profile {ProfileId} ({Name})",
                        profile.Id,
                        profile.Name);
                    deletedCount++;
                }

                if (deletedCount > 0)
                {
                    _logger.LogInformation(
                        "Cleaned up {Count} expired provisional profiles",
                        deletedCount);
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
