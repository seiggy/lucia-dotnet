using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace lucia.Wyoming.Models;

/// <summary>
/// Long-running hosted service that dequeues and executes background work items.
/// Runs for the lifetime of the application host.
/// </summary>
public sealed class BackgroundTaskProcessor(
    IBackgroundTaskQueue taskQueue,
    ILogger<BackgroundTaskProcessor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("[background-task] Processor started, waiting for work items");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var workItem = await taskQueue.DequeueAsync(stoppingToken).ConfigureAwait(false);
                await workItem(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Graceful shutdown
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[background-task] Error executing work item");
            }
        }

        logger.LogInformation("[background-task] Processor stopping");
    }
}
