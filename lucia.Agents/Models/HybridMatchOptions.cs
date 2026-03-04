using lucia.Agents.Abstractions;
using lucia.Agents.Services;

namespace lucia.Agents.Models;

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

    /// <summary>
    /// Penalty applied when the three string-level similarity metrics
    /// (Levenshtein, token-core, phonetic) disagree. Higher values increase
    /// the penalty for spread between the best and mean string scores,
    /// reducing false positives from a single high-scoring metric.
    /// Range 0–1. Set to 0 to disable disagreement penalty.
    /// </summary>
    public double DisagreementPenalty { get; init; } = 0.4;

    /// <summary>
    /// When multiple candidates have embedding similarities within this margin
    /// of each other, string-level scores are used to resolve the tie.
    /// Prevents the embedding signal from drowning out useful string-level
    /// differentiation between similarly-named entities. Range 0–1.
    /// </summary>
    public double EmbeddingResolutionMargin { get; init; } = 0.30;
}
