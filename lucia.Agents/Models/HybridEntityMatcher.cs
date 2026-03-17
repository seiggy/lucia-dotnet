using lucia.Agents.Abstractions;
using lucia.Agents.Integration;
using lucia.Agents.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.Models;

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
    private readonly SearchTermCache _cache = new(capacity: 200);

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

        // Normalize the search term for cache lookup
        var normalizedKey = SearchTermNormalizer.Normalize(searchTerm);

        Embedding<float> searchEmbedding;
        string[] searchPhoneticKeys;

        if (_cache.TryGet(normalizedKey, out var cached))
        {
            searchEmbedding = cached.Embedding;
            searchPhoneticKeys = cached.PhoneticKeys;
        }
        else
        {
            searchEmbedding = await embeddingGenerator.GenerateAsync(
                searchTerm,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            searchPhoneticKeys = StringSimilarity.BuildPhoneticKeys(
                normalizedKey.Length > 0 ? normalizedKey : searchTerm);

            _cache.Put(normalizedKey, new CachedSearchTerm(searchEmbedding, searchPhoneticKeys, normalizedKey));
        }

        // Score every candidate using the hybrid algorithm
        var scored = new List<EntityMatchResult<T>>(candidates.Count);
        foreach (var candidate in candidates)
        {
            var embeddingSim = similarityService.ComputeSimilarity(
                searchEmbedding, candidate.NameEmbedding);

            // Score against primary name (full hybrid: embedding + string)
            var bestScore = StringSimilarity.HybridScore(
                embeddingSim,
                searchTerm,
                candidate.MatchableName,
                options.EmbeddingWeight,
                searchPhoneticKeys,
                candidate.PhoneticKeys,
                options.DisagreementPenalty);

            // Score against each alias using pre-computed phonetic keys
            var aliases = candidate.MatchableAliases;
            var aliasKeys = candidate.AliasPhoneticKeys;
            for (var i = 0; i < aliases.Count; i++)
            {
                var phonKeys = i < aliasKeys.Count ? aliasKeys[i] : [];
                var aliasScore = StringSimilarity.HybridScore(
                    embeddingSim,
                    searchTerm,
                    aliases[i],
                    options.EmbeddingWeight,
                    searchPhoneticKeys,
                    phonKeys,
                    options.DisagreementPenalty);

                if (aliasScore > bestScore)
                    bestScore = aliasScore;
            }

            if (bestScore >= options.Threshold)
            {
                scored.Add(new EntityMatchResult<T>
                {
                    Entity = candidate,
                    HybridScore = bestScore,
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
            var minRelativeScore = topScore * Math.Clamp(options.ScoreDropoffRatio, 0.0, 1.0);
            scored.RemoveAll(x => x.HybridScore < minRelativeScore);
        }

        // Guard against empty list after drop-off filtering (e.g. misconfigured ratio)
        if (scored.Count == 0)
        {
            logger.LogDebug(
                "All matches removed by score drop-off filter for term '{SearchTerm}'",
                searchTerm);
            return [];
        }

        logger.LogDebug(
            "Found {Count} match(es) for '{SearchTerm}' (top score: {TopScore:F4})",
            scored.Count, searchTerm, scored[0].HybridScore);

        return scored;
    }
}
