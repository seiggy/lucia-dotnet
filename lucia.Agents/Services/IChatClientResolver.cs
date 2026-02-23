using Microsoft.Extensions.AI;

namespace lucia.Agents.Services;

/// <summary>
/// Resolves an <see cref="IChatClient"/> from the model provider system.
/// Falls back to the default DI-registered client when no provider is configured.
/// </summary>
public interface IChatClientResolver
{
    /// <summary>
    /// Resolves an <see cref="IChatClient"/> by model provider name.
    /// If <paramref name="providerName"/> is null or empty, returns the default client.
    /// If the provider is not found or disabled, falls back to the default client.
    /// </summary>
    Task<IChatClient> ResolveAsync(string? providerName, CancellationToken ct = default);
}
