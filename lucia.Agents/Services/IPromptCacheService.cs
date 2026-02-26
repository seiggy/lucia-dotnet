using Microsoft.Extensions.AI;
using lucia.Agents.Models;
using lucia.Agents.Orchestration.Models;

namespace lucia.Agents.Services;

/// <summary>
/// Provides Redis-backed caching for routing decisions and agent-level LLM responses
/// with exact and semantic matching.
/// </summary>
public interface IPromptCacheService
{
    // ── Routing cache (router executor) ─────────────────────────────────

    /// <summary>
    /// Try to find a cached routing decision for the given chat messages.
    /// First tries exact SHA256 match, then falls back to semantic similarity.
    /// </summary>
    Task<CachedPromptResponse?> TryGetCachedResponseAsync(IList<ChatMessage> messages, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cache a routing decision for future lookups.
    /// </summary>
    Task CacheRoutingDecisionAsync(IList<ChatMessage> messages, AgentChoiceResult decision, CancellationToken cancellationToken = default);

    // ── Chat response cache (agent-level) ───────────────────────────────

    /// <summary>
    /// Try to find a cached LLM response for the given normalized prompt.
    /// The normalized prompt should include the system instructions and user messages
    /// to differentiate between agents.
    /// </summary>
    Task<CachedChatResponseData?> TryGetCachedChatResponseAsync(string normalizedPrompt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cache an LLM response (text and/or function calls) for a normalized prompt.
    /// Function calls are replayed through the tool invocation layer so tools execute fresh.
    /// </summary>
    Task CacheChatResponseAsync(string normalizedPrompt, CachedChatResponseData data, CancellationToken cancellationToken = default);

    // ── Management ──────────────────────────────────────────────────────

    /// <summary>
    /// Get all cached routing entries for the management UI.
    /// </summary>
    Task<List<CachedPromptEntry>> GetAllCachedEntriesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all cached chat response entries for the management UI.
    /// </summary>
    Task<List<CachedChatResponseData>> GetAllChatCacheEntriesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Evict a single routing cache entry by key.
    /// </summary>
    Task<bool> EvictAsync(string cacheKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Evict a single chat cache entry by key.
    /// </summary>
    Task<bool> EvictChatEntryAsync(string cacheKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Evict all routing cache entries.
    /// </summary>
    Task<long> EvictAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Evict all chat cache entries.
    /// </summary>
    Task<long> EvictAllChatEntriesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get routing cache statistics (total entries, total hits, etc).
    /// </summary>
    Task<PromptCacheStats> GetStatsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get chat cache statistics (total entries, total hits, etc).
    /// </summary>
    Task<PromptCacheStats> GetChatCacheStatsAsync(CancellationToken cancellationToken = default);
}
