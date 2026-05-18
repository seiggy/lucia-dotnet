using Microsoft.Extensions.AI;

namespace lucia.Tests.TestDoubles;

internal sealed class FixedEmbeddingGenerator(Embedding<float> embedding) : IEmbeddingGenerator<string, Embedding<float>>
{
    public Task<Embedding<float>> GenerateAsync(string value, EmbeddingGenerationOptions? options, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(embedding);
    }

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values, EmbeddingGenerationOptions? options, CancellationToken cancellationToken = default)
    {
        var generatedEmbeddings = values.Select(_ => embedding).ToList();
        return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(generatedEmbeddings));
    }

    public object? GetService(Type serviceType, object? serviceKey) => null;

    public void Dispose()
    {
    }
}
