namespace lucia.Agents.Services;

/// <summary>
/// A single match returned by <see cref="IHybridEntityMatcher.FindMatchesAsync{T}"/>,
/// wrapping the matched entity with its computed similarity scores.
/// Results are returned in descending order of <see cref="HybridScore"/>.
/// </summary>
public sealed record EntityMatchResult<T> where T : IMatchableEntity
{
    /// <summary>The matched entity.</summary>
    public required T Entity { get; init; }

    /// <summary>
    /// The blended hybrid score that combined embedding and string similarity.
    /// This is the score used for threshold filtering and ranking.
    /// </summary>
    public required double HybridScore { get; init; }

    /// <summary>
    /// The raw embedding cosine similarity before blending.
    /// Useful for diagnostics and logging.
    /// </summary>
    public required double EmbeddingSimilarity { get; init; }
}
