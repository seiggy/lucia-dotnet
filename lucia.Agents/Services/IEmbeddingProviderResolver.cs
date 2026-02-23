using Microsoft.Extensions.AI;

namespace lucia.Agents.Services;

/// <summary>
/// Resolves embedding generators from the model provider system.
/// Skills and services use this to get an <see cref="IEmbeddingGenerator{String, Embedding}"/>
/// for vector search operations.
/// </summary>
public interface IEmbeddingProviderResolver
{
    /// <summary>
    /// Resolves an embedding generator. If <paramref name="providerName"/> is specified,
    /// that specific provider is used. Otherwise, the first enabled system-default
    /// embedding provider is returned.
    /// </summary>
    /// <param name="providerName">Optional provider ID (from AgentDefinition.EmbeddingProviderName).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The embedding generator, or null if no provider is configured.</returns>
    Task<IEmbeddingGenerator<string, Embedding<float>>?> ResolveAsync(
        string? providerName = null,
        CancellationToken ct = default);
}
