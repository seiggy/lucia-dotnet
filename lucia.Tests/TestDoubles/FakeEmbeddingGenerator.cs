using Microsoft.Extensions.AI;

namespace lucia.Tests.TestDoubles;

/// <summary>
/// Returns constant-value embeddings so skills that require an
/// <see cref="IEmbeddingGenerator{String, Embedding}"/> can populate their
/// device caches during eval harness runs. The embeddings are NOT semantically
/// meaningful — they only prevent the null-embedding early-return paths.
/// Actual entity matching in eval falls through to the
/// <see cref="SnapshotEntityLocationService"/> substring fallback.
/// </summary>
internal sealed class FakeEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    private const int Dimensions = 384;
    private static readonly float[] ConstantVector = CreateConstantVector();

    public EmbeddingGeneratorMetadata Metadata { get; } =
        new("FakeEmbeddingGenerator");

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var results = values.Select(_ => new Embedding<float>(ConstantVector)).ToList();
        return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(results));
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
        => serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose() { }

    private static float[] CreateConstantVector()
    {
        var v = new float[Dimensions];
        Array.Fill(v, 1.0f / MathF.Sqrt(Dimensions));
        return v;
    }
}
