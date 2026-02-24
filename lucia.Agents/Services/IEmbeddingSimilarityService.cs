using Microsoft.Extensions.AI;

namespace lucia.Agents.Services;

/// <summary>
/// Provides cosine similarity computation between embedding vectors.
/// Centralises the comparison logic so individual skills don't each maintain
/// their own implementation.
/// </summary>
public interface IEmbeddingSimilarityService
{
    /// <summary>
    /// Computes cosine similarity between two <see cref="Embedding{T}"/> instances.
    /// Returns a value between -1 and 1, where 1 means identical direction.
    /// </summary>
    double ComputeSimilarity(Embedding<float>? a, Embedding<float>? b);
}
