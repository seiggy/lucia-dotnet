using Microsoft.Extensions.Hosting;

namespace lucia.Wyoming.Models;

/// <summary>
/// Loads persisted model preferences from MongoDB during application startup,
/// ensuring that user-selected STT engine and model choices survive reboots.
/// </summary>
public sealed class ModelPreferenceInitializer(ModelManager modelManager) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await modelManager.LoadPersistedPreferencesAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
