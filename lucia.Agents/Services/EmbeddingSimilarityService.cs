using System.Numerics.Tensors;
using Microsoft.Extensions.AI;

namespace lucia.Agents.Services;

/// <summary>
/// Hardware-accelerated cosine similarity using <see cref="TensorPrimitives"/>.
/// Registered as a singleton — stateless and thread-safe.
/// </summary>
public sealed class EmbeddingSimilarityService : IEmbeddingSimilarityService
{
    public double ComputeSimilarity(Embedding<float>? a, Embedding<float>? b)
    {
        if (a is null || b is null)
            return 0.0;

        var spanA = a.Vector.Span;
        var spanB = b.Vector.Span;

        if (spanA.Length != spanB.Length || spanA.Length == 0)
            return 0.0;

        return TensorPrimitives.CosineSimilarity(spanA, spanB);
    }
}
