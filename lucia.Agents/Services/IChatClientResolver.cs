using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace lucia.Agents.Services;

/// <summary>
/// Resolves an <see cref="IChatClient"/> or <see cref="AIAgent"/> from the model provider system.
/// Falls back to the default DI-registered client when no provider is configured.
/// </summary>
public interface IChatClientResolver
{
    /// <summary>
    /// Resolves an <see cref="IChatClient"/> by model provider name.
    /// If <paramref name="providerName"/> is null or empty, returns the default client.
    /// Not valid for <see cref="Configuration.ProviderType.GitHubCopilot"/> providers —
    /// use <see cref="ResolveAIAgentAsync"/> instead.
    /// </summary>
    Task<IChatClient> ResolveAsync(string? providerName, CancellationToken ct = default);

    /// <summary>
    /// Attempts to resolve an <see cref="AIAgent"/> directly for providers that produce agents
    /// (e.g. GitHub Copilot). Returns <c>null</c> for standard providers that use <see cref="IChatClient"/>.
    /// </summary>
    Task<AIAgent?> ResolveAIAgentAsync(string? providerName, CancellationToken ct = default);
}
