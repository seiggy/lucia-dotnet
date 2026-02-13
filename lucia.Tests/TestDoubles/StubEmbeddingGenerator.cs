using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace lucia.Tests.TestDoubles;

internal sealed class StubEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    public Task<Embedding<float>> GenerateAsync(string value, EmbeddingGenerationOptions? options, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(CreateEmbedding(new[] { 1f, 0.5f, 0.25f }));
    }

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values, EmbeddingGenerationOptions? options, CancellationToken cancellationToken = default)
    {
        var embeddings = values.Select(_ => CreateEmbedding(new[] { 1f, 0.5f, 0.25f })).ToList();
        return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(embeddings));
    }

    public object? GetService(Type serviceType, object? serviceKey) => null;

    public void Dispose()
    {
    }

    private static Embedding<float> CreateEmbedding(float[] values)
    {
        return new Embedding<float>(values);
    }
}
