namespace lucia.Agents.Services;

/// <summary>
/// Configuration for a single <see cref="IHybridEntityMatcher.FindMatchesAsync{T}"/> call.
/// Each skill provides its own options (typically mapped from its
/// <c>IOptionsMonitor&lt;T&gt;</c> at call time).
/// </summary>
public sealed record HybridMatchOptions
{
    /// <summary>
    /// Minimum hybrid similarity score (0–1) for an entity to be included
    /// in search results. The hybrid score blends embedding cosine similarity
    /// with string-level similarity.
    /// </summary>
    public double Threshold { get; init; } = 0.55;

    /// <summary>
    /// Weight applied to the embedding cosine similarity component (0–1).
    /// The string similarity weight is <c>1 − EmbeddingWeight</c>.
    /// </summary>
    public double EmbeddingWeight { get; init; } = 0.4;

    /// <summary>
    /// After sorting by score, only keep results whose score is at least
    /// this fraction of the top match's score. For example 0.80 means a
    /// result must score ≥ 80 % of the best match to be included.
    /// Set to 0 to disable relative filtering.
    /// </summary>
    public double ScoreDropoffRatio { get; init; } = 0.80;
}
