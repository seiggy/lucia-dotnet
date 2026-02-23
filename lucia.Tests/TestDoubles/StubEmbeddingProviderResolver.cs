using lucia.Agents.Services;
using Microsoft.Extensions.AI;

namespace lucia.Tests.TestDoubles;

/// <summary>
/// Test-friendly <see cref="IEmbeddingProviderResolver"/> that always returns a
/// pre-configured <see cref="IEmbeddingGenerator{String, Embedding}"/>.
/// </summary>
internal sealed class StubEmbeddingProviderResolver : IEmbeddingProviderResolver
{
    private readonly IEmbeddingGenerator<string, Embedding<float>>? _generator;

    public StubEmbeddingProviderResolver(IEmbeddingGenerator<string, Embedding<float>>? generator = null)
    {
        _generator = generator;
    }

    public Task<IEmbeddingGenerator<string, Embedding<float>>?> ResolveAsync(
        string? providerName = null,
        CancellationToken ct = default)
    {
        return Task.FromResult(_generator);
    }
}
