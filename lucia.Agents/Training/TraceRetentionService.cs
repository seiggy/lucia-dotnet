using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucia.Agents.Training;

/// <summary>
/// Background service that periodically purges unlabeled traces older than the configured retention period.
/// </summary>
public sealed class TraceRetentionService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly TraceCaptureOptions _options;
    private readonly ILogger<TraceRetentionService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromHours(24);

    public TraceRetentionService(
        IServiceProvider serviceProvider,
        IOptions<TraceCaptureOptions> options,
        ILogger<TraceRetentionService> logger)
    {
        _serviceProvider = serviceProvider;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Trace capture is disabled; retention service will not run");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var repository = scope.ServiceProvider.GetRequiredService<ITraceRepository>();
                var deletedCount = await repository.DeleteOldUnlabeledAsync(_options.RetentionDays, stoppingToken).ConfigureAwait(false);
                
                if (deletedCount > 0)
                {
                    _logger.LogInformation("Retention cleanup: purged {Count} unlabeled traces older than {Days} days",
                        deletedCount, _options.RetentionDays);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during trace retention cleanup");
            }

            await Task.Delay(_interval, stoppingToken).ConfigureAwait(false);
        }
    }
}
