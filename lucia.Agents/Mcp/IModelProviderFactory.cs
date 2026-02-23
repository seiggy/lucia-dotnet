using lucia.Agents.Configuration;
using Microsoft.Extensions.AI;

namespace lucia.Agents.Mcp;

/// <summary>
/// Creates IChatClient instances from stored ModelProvider configurations.
/// </summary>
public interface IModelProviderFactory
{
    /// <summary>
    /// Creates an IChatClient for the given provider configuration.
    /// The returned client includes OpenTelemetry and logging middleware.
    /// </summary>
    IChatClient CreateClient(ModelProvider provider);

    /// <summary>
    /// Sends a simple test message to verify connectivity and returns a result.
    /// </summary>
    Task<ModelProviderTestResult> TestConnectionAsync(ModelProvider provider, CancellationToken ct = default);
}

/// <summary>
/// Result of a model provider connection test.
/// </summary>
public sealed record ModelProviderTestResult(bool Success, string Message);
