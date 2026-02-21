using lucia.Agents.Orchestration.Models;

namespace lucia.Agents.Models;

/// <summary>
/// Result of a prompt cache lookup, containing the cached routing decision and match metadata.
/// </summary>
public sealed class CachedPromptResponse
{
    /// <summary>The cached routing decision (selected agent, confidence, reasoning).</summary>
    public required AgentChoiceResult RoutingDecision { get; set; }

    /// <summary>Whether this was an exact hash match or semantic similarity match.</summary>
    public bool IsExactMatch { get; set; }

    /// <summary>Cosine similarity score (1.0 for exact match).</summary>
    public double SimilarityScore { get; set; }

    /// <summary>The cache key that matched.</summary>
    public string MatchedCacheKey { get; set; } = string.Empty;
}
