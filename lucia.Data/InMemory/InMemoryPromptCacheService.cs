using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using lucia.Agents.Abstractions;
using lucia.Agents.Models;
using lucia.Agents.Orchestration;
using lucia.Agents.Orchestration.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucia.Data.InMemory;

/// <summary>
/// In-memory implementation of <see cref="IPromptCacheService"/> with exact SHA256 and semantic similarity matching.
/// Replaces the Redis-backed version for lightweight/mono-container deployments.
/// </summary>
public sealed class InMemoryPromptCacheService : IPromptCacheService, IDisposable
{
    private const int DefaultCacheTtlHours = 48;

    private readonly ConcurrentDictionary<string, CachedPromptEntry> _routingEntries = new();
    private readonly ConcurrentDictionary<string, CachedChatResponseData> _chatEntries = new();

    private readonly IEmbeddingProviderResolver _embeddingResolver;
    private readonly IEmbeddingSimilarityService _embeddingSimilarityService;
    private readonly IOptionsMonitor<RouterExecutorOptions> _routerOptions;
    private readonly ILogger<InMemoryPromptCacheService> _logger;
    private readonly TimeSpan _cacheTtl;
    private readonly Timer _cleanupTimer;

    private IEmbeddingGenerator<string, Embedding<float>>? _embeddingGenerator;

    private long _routingHits;
    private long _routingMisses;
    private long _chatHits;
    private long _chatMisses;

    public InMemoryPromptCacheService(
        IEmbeddingProviderResolver embeddingResolver,
        IEmbeddingSimilarityService embeddingSimilarityService,
        IOptionsMonitor<RouterExecutorOptions> routerOptions,
        ILogger<InMemoryPromptCacheService> logger)
    {
        _embeddingResolver = embeddingResolver;
        _embeddingSimilarityService = embeddingSimilarityService;
        _routerOptions = routerOptions;
        _logger = logger;
        _cacheTtl = TimeSpan.FromHours(DefaultCacheTtlHours);

        // Periodic cleanup every 10 minutes
        _cleanupTimer = new Timer(EvictExpiredEntries, null, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));
    }

    // ── Routing cache ───────────────────────────────────────────────────

    public async Task<CachedPromptResponse?> TryGetCachedRoutingDecisionAsync(
        IList<ChatMessage> messages,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var normalizedPrompt = NormalizePrompt(messages);
            var hash = ComputeSha256(normalizedPrompt);

            // Exact match
            if (_routingEntries.TryGetValue(hash, out var entry) && entry.AgentId is { Length: > 0 })
            {
                if (IsExpired(entry.CreatedAt))
                {
                    _routingEntries.TryRemove(hash, out _);
                }
                else
                {
                    entry.HitCount++;
                    entry.LastHitAt = DateTime.UtcNow;
                    Interlocked.Increment(ref _routingHits);

                    return new CachedPromptResponse
                    {
                        RoutingDecision = ToAgentChoiceResult(entry),
                        IsExactMatch = true,
                        SimilarityScore = 1.0,
                        MatchedCacheKey = entry.CacheKey
                    };
                }
            }

            // Semantic similarity fallback
            if (_routingEntries.IsEmpty)
            {
                Interlocked.Increment(ref _routingMisses);
                return null;
            }

            var queryEmbedding = await GenerateEmbeddingAsync(normalizedPrompt, cancellationToken).ConfigureAwait(false);
            if (queryEmbedding is null)
            {
                Interlocked.Increment(ref _routingMisses);
                return null;
            }

            CachedPromptEntry? bestEntry = null;
            double bestScore = 0;

            foreach (var kvp in _routingEntries)
            {
                if (IsExpired(kvp.Value.CreatedAt))
                    continue;

                if (kvp.Value.Embedding is null)
                    continue;

                var candidateEmbedding = new Embedding<float>(kvp.Value.Embedding);
                var score = _embeddingSimilarityService.ComputeSimilarity(queryEmbedding, candidateEmbedding);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestEntry = kvp.Value;
                }
            }

            var routingThreshold = _routerOptions.CurrentValue.SemanticSimilarityThreshold;
            if (bestEntry is not null && bestScore >= routingThreshold)
            {
                bestEntry.HitCount++;
                bestEntry.LastHitAt = DateTime.UtcNow;
                Interlocked.Increment(ref _routingHits);

                return new CachedPromptResponse
                {
                    RoutingDecision = ToAgentChoiceResult(bestEntry),
                    IsExactMatch = false,
                    SimilarityScore = bestScore,
                    MatchedCacheKey = bestEntry.CacheKey
                };
            }

            Interlocked.Increment(ref _routingMisses);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during in-memory prompt cache lookup");
            return null;
        }
    }

    public async Task CacheRoutingDecisionAsync(
        IList<ChatMessage> messages,
        AgentChoiceResult decision,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var normalizedPrompt = NormalizePrompt(messages);
            var hash = ComputeSha256(normalizedPrompt);

            var embedding = await GenerateEmbeddingAsync(normalizedPrompt, cancellationToken).ConfigureAwait(false);

            var entry = new CachedPromptEntry
            {
                CacheKey = hash,
                NormalizedPrompt = normalizedPrompt,
                AgentId = decision.AgentId,
                Confidence = decision.Confidence,
                Reasoning = decision.Reasoning,
                AdditionalAgents = decision.AdditionalAgents,
                Embedding = embedding?.Vector.ToArray(),
                HitCount = 0,
                CreatedAt = DateTime.UtcNow,
                LastHitAt = DateTime.UtcNow
            };

            _routingEntries[hash] = entry;

            _logger.LogInformation(
                "Cached routing decision for key {CacheKey}: agent={AgentId}, confidence={Confidence:F2}",
                hash, decision.AgentId, decision.Confidence);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error caching routing decision");
        }
    }

    // ── Chat response cache ─────────────────────────────────────────────

    public async Task<CachedChatResponseData?> TryGetCachedChatResponseAsync(
        string normalizedPrompt,
        string? semanticQueryText = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var hash = ComputeSha256(normalizedPrompt);

            // Exact match
            if (_chatEntries.TryGetValue(hash, out var entry)
                && (entry.ResponseText is not null || entry.FunctionCalls is { Count: > 0 }))
            {
                if (IsExpired(entry.CreatedAt))
                {
                    _chatEntries.TryRemove(hash, out _);
                }
                else
                {
                    entry.HitCount++;
                    entry.LastHitAt = DateTime.UtcNow;
                    Interlocked.Increment(ref _chatHits);
                    return entry;
                }
            }

            // Semantic similarity fallback
            if (_chatEntries.IsEmpty)
            {
                Interlocked.Increment(ref _chatMisses);
                return null;
            }

            var embeddingInput = semanticQueryText ?? normalizedPrompt;
            var queryEmbedding = await GenerateEmbeddingAsync(embeddingInput, cancellationToken).ConfigureAwait(false);
            if (queryEmbedding is null)
            {
                Interlocked.Increment(ref _chatMisses);
                return null;
            }

            CachedChatResponseData? bestEntry = null;
            double bestScore = 0;

            foreach (var kvp in _chatEntries)
            {
                if (IsExpired(kvp.Value.CreatedAt))
                    continue;

                if (kvp.Value.Embedding is null)
                    continue;

                var candidateEmbedding = new Embedding<float>(kvp.Value.Embedding);
                var score = _embeddingSimilarityService.ComputeSimilarity(queryEmbedding, candidateEmbedding);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestEntry = kvp.Value;
                }
            }

            var chatThreshold = _routerOptions.CurrentValue.ChatCacheSemanticThreshold;
            if (bestEntry is not null && bestScore >= chatThreshold)
            {
                bestEntry.HitCount++;
                bestEntry.LastHitAt = DateTime.UtcNow;
                Interlocked.Increment(ref _chatHits);
                return bestEntry;
            }

            Interlocked.Increment(ref _chatMisses);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during in-memory chat cache lookup");
            return null;
        }
    }

    public async Task CacheChatResponseAsync(
        string normalizedPrompt,
        CachedChatResponseData data,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var hash = ComputeSha256(normalizedPrompt);

            data.CacheKey = hash;
            if (string.IsNullOrEmpty(data.NormalizedPrompt))
                data.NormalizedPrompt = normalizedPrompt;

            var embeddingText = data.NormalizedPrompt;
            var embedding = await GenerateEmbeddingAsync(embeddingText, cancellationToken).ConfigureAwait(false);
            data.Embedding = embedding?.Vector.ToArray();
            data.CreatedAt = DateTime.UtcNow;
            data.LastHitAt = DateTime.UtcNow;

            _chatEntries[hash] = data;

            _logger.LogInformation(
                "Cached chat response for key {CacheKey}: hasText={HasText}, functionCalls={FunctionCallCount}",
                hash, data.ResponseText is not null, data.FunctionCalls?.Count ?? 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error caching chat response");
        }
    }

    // ── Management ──────────────────────────────────────────────────────

    public Task<List<CachedPromptEntry>> GetAllCachedEntriesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_routingEntries.Values.ToList());

    public Task<List<CachedChatResponseData>> GetAllChatCacheEntriesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_chatEntries.Values.ToList());

    public Task<bool> EvictAsync(string cacheKey, CancellationToken cancellationToken = default)
        => Task.FromResult(_routingEntries.TryRemove(cacheKey, out _));

    public Task<bool> EvictChatEntryAsync(string cacheKey, CancellationToken cancellationToken = default)
        => Task.FromResult(_chatEntries.TryRemove(cacheKey, out _));

    public Task<long> EvictAllAsync(CancellationToken cancellationToken = default)
    {
        long count = _routingEntries.Count;
        _routingEntries.Clear();
        Interlocked.Exchange(ref _routingHits, 0);
        Interlocked.Exchange(ref _routingMisses, 0);
        return Task.FromResult(count);
    }

    public Task<long> EvictAllChatEntriesAsync(CancellationToken cancellationToken = default)
    {
        long count = _chatEntries.Count;
        _chatEntries.Clear();
        Interlocked.Exchange(ref _chatHits, 0);
        Interlocked.Exchange(ref _chatMisses, 0);
        return Task.FromResult(count);
    }

    public Task<PromptCacheStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new PromptCacheStats
        {
            TotalEntries = _routingEntries.Count,
            TotalHits = Interlocked.Read(ref _routingHits),
            TotalMisses = Interlocked.Read(ref _routingMisses)
        });
    }

    public Task<PromptCacheStats> GetChatCacheStatsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new PromptCacheStats
        {
            TotalEntries = _chatEntries.Count,
            TotalHits = Interlocked.Read(ref _chatHits),
            TotalMisses = Interlocked.Read(ref _chatMisses)
        });
    }

    public void Dispose()
    {
        _cleanupTimer.Dispose();
    }

    // ── Private helpers ─────────────────────────────────────────────────

    private bool IsExpired(DateTime createdAt) => DateTime.UtcNow - createdAt > _cacheTtl;

    private void EvictExpiredEntries(object? state)
    {
        var routingRemoved = 0;
        foreach (var kvp in _routingEntries)
        {
            if (IsExpired(kvp.Value.CreatedAt))
            {
                if (_routingEntries.TryRemove(kvp.Key, out _))
                    routingRemoved++;
            }
        }

        var chatRemoved = 0;
        foreach (var kvp in _chatEntries)
        {
            if (IsExpired(kvp.Value.CreatedAt))
            {
                if (_chatEntries.TryRemove(kvp.Key, out _))
                    chatRemoved++;
            }
        }

        if (routingRemoved > 0 || chatRemoved > 0)
        {
            _logger.LogDebug("TTL cleanup: removed {RoutingCount} routing + {ChatCount} chat entries",
                routingRemoved, chatRemoved);
        }
    }

    private static AgentChoiceResult ToAgentChoiceResult(CachedPromptEntry entry) => new()
    {
        AgentId = entry.AgentId,
        Confidence = entry.Confidence,
        Reasoning = entry.Reasoning ?? "[cached routing decision]",
        AdditionalAgents = entry.AdditionalAgents
    };

    private static string NormalizePrompt(IList<ChatMessage> messages)
    {
        var userMessage = messages.LastOrDefault(m => m.Role == ChatRole.User);
        if (userMessage is null)
            return string.Empty;

        var promptLineArray = userMessage.Text.Split(["\r\n", "\n"], StringSplitOptions.None);
        for (var i = promptLineArray.Length - 1; i >= 0; i--)
        {
            if (string.IsNullOrWhiteSpace(promptLineArray[i])) continue;
            return promptLineArray[i].Trim().ToLowerInvariant();
        }

        return string.Empty;
    }

    private static string ComputeSha256(string input)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(hashBytes);
    }

    private async Task<Embedding<float>?> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken)
    {
        try
        {
            _embeddingGenerator ??= await _embeddingResolver.ResolveAsync(ct: cancellationToken).ConfigureAwait(false);
            if (_embeddingGenerator is null)
            {
                _logger.LogWarning("No embedding provider configured — prompt cache similarity search disabled");
                return null;
            }

            return await _embeddingGenerator.GenerateAsync(text, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate embedding for prompt cache");
            return null;
        }
    }
}
