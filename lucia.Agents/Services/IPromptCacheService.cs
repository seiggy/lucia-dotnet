using Microsoft.Extensions.AI;
using lucia.Agents.Models;
using lucia.Agents.Orchestration.Models;

namespace lucia.Agents.Services;

/// <summary>
/// Provides Redis-backed caching for routing decisions with exact and semantic matching.
/// Caches which agent should handle a given prompt (not the final response),
/// so that subsequent identical/similar prompts skip the router LLM call
/// but agents still execute tools fresh every time.
/// </summary>
public interface IPromptCacheService
{
    /// <summary>
    /// Try to find a cached routing decision for the given chat messages.
    /// First tries exact SHA256 match, then falls back to semantic similarity.
    /// </summary>
    Task<CachedPromptResponse?> TryGetCachedResponseAsync(IList<ChatMessage> messages, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cache a routing decision for future lookups.
    /// </summary>
    Task CacheRoutingDecisionAsync(IList<ChatMessage> messages, AgentChoiceResult decision, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all cached entries for the management UI.
    /// </summary>
    Task<List<CachedPromptEntry>> GetAllCachedEntriesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Evict a single cache entry by key.
    /// </summary>
    Task<bool> EvictAsync(string cacheKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Evict all cached entries.
    /// </summary>
    Task<long> EvictAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get cache statistics (total entries, total hits, etc).
    /// </summary>
    Task<PromptCacheStats> GetStatsAsync(CancellationToken cancellationToken = default);
}
