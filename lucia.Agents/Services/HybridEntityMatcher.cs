using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.Services;

/// <summary>
/// Default implementation of <see cref="IHybridEntityMatcher"/> that blends
/// embedding cosine similarity with string-level similarity (Levenshtein,
/// token-core, and phonetic) to produce accurate fuzzy-match results for
/// natural-language entity searches.
/// </summary>
public sealed class HybridEntityMatcher(
    IEmbeddingSimilarityService similarityService,
    ILogger<HybridEntityMatcher> logger) : IHybridEntityMatcher
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<EntityMatchResult<T>>> FindMatchesAsync<T>(
        string searchTerm,
        IReadOnlyList<T> candidates,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        HybridMatchOptions options,
        CancellationToken cancellationToken = default) where T : IMatchableEntity
    {
        if (candidates.Count == 0)
            return [];

        // Generate embedding for the search term
        var searchEmbedding = await embeddingGenerator.GenerateAsync(
            searchTerm, cancellationToken: cancellationToken).ConfigureAwait(false);

        // Pre-compute search term phonetic keys once
        var searchPhoneticKeys = StringSimilarity.BuildPhoneticKeys(searchTerm);

        // Score every candidate using the hybrid algorithm
        var scored = new List<EntityMatchResult<T>>(candidates.Count);
        foreach (var candidate in candidates)
        {
            var embeddingSim = similarityService.ComputeSimilarity(
                searchEmbedding, candidate.NameEmbedding);

            var hybridSim = StringSimilarity.HybridScore(
                embeddingSim,
                searchTerm,
                candidate.MatchableName,
                options.EmbeddingWeight,
                searchPhoneticKeys,
                candidate.PhoneticKeys);

            if (hybridSim >= options.Threshold)
            {
                scored.Add(new EntityMatchResult<T>
                {
                    Entity = candidate,
                    HybridScore = hybridSim,
                    EmbeddingSimilarity = embeddingSim
                });
            }
        }

        if (scored.Count == 0)
        {
            logger.LogDebug(
                "No entity met hybrid similarity threshold {Threshold} for term '{SearchTerm}'",
                options.Threshold, searchTerm);
            return [];
        }

        // Sort descending by hybrid score
        scored.Sort((a, b) => b.HybridScore.CompareTo(a.HybridScore));

        // Relative score drop-off: keep only results within X% of the top match
        if (scored.Count > 1 && options.ScoreDropoffRatio > 0)
        {
            var topScore = scored[0].HybridScore;
            var minRelativeScore = topScore * options.ScoreDropoffRatio;
            scored.RemoveAll(x => x.HybridScore < minRelativeScore);
        }

        logger.LogDebug(
            "Found {Count} match(es) for '{SearchTerm}' (top score: {TopScore:F4})",
            scored.Count, searchTerm, scored[0].HybridScore);

        return scored;
    }
}
