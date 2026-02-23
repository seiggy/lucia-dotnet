using lucia.Agents.Configuration;
using lucia.Agents.Mcp;
using lucia.Agents.Models;
using Microsoft.Extensions.AI;

namespace lucia.Agents.Abstractions;

/// <summary>
/// Creates IChatClient and IEmbeddingGenerator instances from stored ModelProvider configurations.
/// </summary>
public interface IModelProviderResolver
{
    /// <summary>
    /// Creates an IChatClient for the given provider configuration.
    /// The returned client includes OpenTelemetry and logging middleware.
    /// </summary>
    IChatClient CreateClient(ModelProvider provider);

    /// <summary>
    /// Creates an IEmbeddingGenerator for the given provider configuration.
    /// Only valid for providers with <see cref="ModelPurpose.Embedding"/> purpose.
    /// </summary>
    IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator(ModelProvider provider);

    /// <summary>
    /// Sends a simple test message to verify chat connectivity and returns a result.
    /// </summary>
    Task<ModelProviderTestResult> TestConnectionAsync(ModelProvider provider, CancellationToken ct = default);

    /// <summary>
    /// Generates a test embedding to verify embedding connectivity and returns a result.
    /// </summary>
    Task<ModelProviderTestResult> TestEmbeddingConnectionAsync(ModelProvider provider, CancellationToken ct = default);
}