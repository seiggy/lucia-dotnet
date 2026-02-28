using Microsoft.Extensions.AI;

namespace lucia.Agents.Services;

/// <summary>
/// A generic entity-matching service that finds the best matches for a
/// natural-language search term from a collection of <see cref="IMatchableEntity"/>
/// candidates using a hybrid similarity algorithm combining:
/// <list type="bullet">
///   <item>Embedding cosine similarity (semantic meaning)</item>
///   <item>Normalized Levenshtein distance (character-level closeness)</item>
///   <item>Token-core fuzzy matching (discriminating words, minus stop-words)</item>
///   <item>Metaphone phonetic similarity (pronunciation-based matching)</item>
///   <item>Relative score drop-off filtering (eliminates long-tail false positives)</item>
/// </list>
/// </summary>
public interface IHybridEntityMatcher
{
    /// <summary>
    /// Find entities matching the given search term, ranked by hybrid similarity.
    /// </summary>
    /// <typeparam name="T">Entity type implementing <see cref="IMatchableEntity"/>.</typeparam>
    /// <param name="searchTerm">Natural-language name or description to search for.</param>
    /// <param name="candidates">The cached collection of entities to search.</param>
    /// <param name="embeddingGenerator">
    /// The embedding generator to use for creating the search term embedding.
    /// Each skill provides its own resolved provider.
    /// </param>
    /// <param name="options">Matching parameters (threshold, weights, drop-off).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Matching entities sorted by descending hybrid score, filtered by both the
    /// absolute threshold and relative drop-off ratio.
    /// Returns an empty list if no candidates exceed the threshold.
    /// </returns>
    Task<IReadOnlyList<EntityMatchResult<T>>> FindMatchesAsync<T>(
        string searchTerm,
        IReadOnlyList<T> candidates,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        HybridMatchOptions options,
        CancellationToken cancellationToken = default) where T : IMatchableEntity;
}
