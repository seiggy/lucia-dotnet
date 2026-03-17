using System.Threading.Channels;

namespace lucia.Wyoming.Models;

/// <summary>
/// Queue for background work items processed by <see cref="BackgroundTaskProcessor"/>.
/// Follows the standard ASP.NET Core IBackgroundTaskQueue pattern.
/// </summary>
public interface IBackgroundTaskQueue
{
    ValueTask QueueBackgroundWorkItemAsync(Func<CancellationToken, ValueTask> workItem);

    ValueTask<Func<CancellationToken, ValueTask>> DequeueAsync(CancellationToken cancellationToken);
}
