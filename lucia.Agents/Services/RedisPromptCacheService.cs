using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

using lucia.Agents.Models;
using lucia.Agents.Orchestration.Models;

using StackExchange.Redis;

namespace lucia.Agents.Services;

/// <summary>
/// Redis-backed prompt cache with exact SHA256 and semantic similarity matching.
/// Caches routing decisions (not final responses) so agents still execute tools.
/// </summary>
public sealed class RedisPromptCacheService : IPromptCacheService
{
    // Routing cache (router executor)
    private const string KeyPrefix = "lucia:prompt-cache:";
    private const string IndexKey = "lucia:prompt-cache:index";
    private const string StatsHitsKey = "lucia:prompt-cache:stats:hits";
    private const string StatsMissesKey = "lucia:prompt-cache:stats:misses";

    // Chat response cache entries persist until manually evicted
    private const string ChatKeyPrefix = "lucia:chat-cache:";
    private const string ChatIndexKey = "lucia:chat-cache:index";
    private const string ChatStatsHitsKey = "lucia:chat-cache:stats:hits";
    private const string ChatStatsMissesKey = "lucia:chat-cache:stats:misses";

    // Strips volatile HA context fields so identical intents produce the same cache key.
    private static readonly Regex VolatileHaFieldsPattern = new(
        @"""(?:timestamp|day_of_week|id)"":\s*""[^""]*""",
        RegexOptions.Compiled);

    // High threshold required — home automation prompts differ by a single noun
    // (e.g., "kitchen lights" vs "office lights" score ~0.95 with embeddings)
    private const double SemanticSimilarityThreshold = 0.99;

    private static readonly ActivitySource ActivitySource = new("Lucia.Services.PromptCache", "1.0.0");
    private static readonly Meter Meter = new("Lucia.Services.PromptCache", "1.0.0");

    private static readonly Counter<long> HitsCounter = Meter.CreateCounter<long>("prompt.cache.hits");
    private static readonly Counter<long> MissesCounter = Meter.CreateCounter<long>("prompt.cache.misses");
    private static readonly Counter<long> SemanticHitsCounter = Meter.CreateCounter<long>("prompt.cache.semantic_hits");
    private static readonly Histogram<double> LookupDuration = Meter.CreateHistogram<double>("prompt.cache.lookup.duration", "ms");
    private static readonly Counter<long> ChatHitsCounter = Meter.CreateCounter<long>("chat.cache.hits");
    private static readonly Counter<long> ChatMissesCounter = Meter.CreateCounter<long>("chat.cache.misses");
    private static readonly Histogram<double> ChatLookupDuration = Meter.CreateHistogram<double>("chat.cache.lookup.duration", "ms");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IConnectionMultiplexer _redis;
    private readonly IEmbeddingProviderResolver _embeddingResolver;
    private IEmbeddingGenerator<string, Embedding<float>>? _embeddingGenerator;
    private readonly ILogger<RedisPromptCacheService> _logger;

    public RedisPromptCacheService(
        IConnectionMultiplexer redis,
        IEmbeddingProviderResolver embeddingResolver,
        ILogger<RedisPromptCacheService> logger)
    {
        _redis = redis;
        _embeddingResolver = embeddingResolver;
        _logger = logger;
    }

    public async Task<CachedPromptResponse?> TryGetCachedResponseAsync(
        IList<ChatMessage> messages,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("PromptCache.Lookup");
        var sw = Stopwatch.StartNew();

        try
        {
            var db = _redis.GetDatabase();
            var normalizedPrompt = NormalizePrompt(messages);
            var hash = ComputeSha256(normalizedPrompt);
            var cacheKey = $"{KeyPrefix}{hash}";

            activity?.SetTag("cache.key", cacheKey);

            // Exact match
            var exactValue = await db.StringGetAsync(cacheKey).ConfigureAwait(false);
            if (exactValue.HasValue)
            {
                var entry = JsonSerializer.Deserialize<CachedPromptEntry>(exactValue.ToString(), SerializerOptions);
                if (entry is not null && !string.IsNullOrWhiteSpace(entry.AgentId))
                {
                    entry.HitCount++;
                    entry.LastHitAt = DateTime.UtcNow;
                    await db.StringSetAsync(cacheKey, JsonSerializer.Serialize(entry, SerializerOptions)).ConfigureAwait(false);
                    await db.StringIncrementAsync(StatsHitsKey).ConfigureAwait(false);
                    HitsCounter.Add(1);
                    activity?.SetTag("cache.hit", true);
                    activity?.SetTag("cache.match_type", "exact");

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
            var indexMembers = await db.SetMembersAsync(IndexKey).ConfigureAwait(false);
            if (indexMembers.Length == 0)
            {
                await db.StringIncrementAsync(StatsMissesKey).ConfigureAwait(false);
                MissesCounter.Add(1);
                activity?.SetTag("cache.hit", false);
                return null;
            }

            var queryEmbedding = await GenerateEmbeddingAsync(normalizedPrompt, cancellationToken).ConfigureAwait(false);
            if (queryEmbedding is null)
            {
                await db.StringIncrementAsync(StatsMissesKey).ConfigureAwait(false);
                MissesCounter.Add(1);
                return null;
            }

            CachedPromptEntry? bestEntry = null;
            double bestScore = 0;

            var memberKeys = indexMembers.Select(m => (RedisKey)m.ToString()).ToArray();
            var values = await db.StringGetAsync(memberKeys).ConfigureAwait(false);

            for (var i = 0; i < values.Length; i++)
            {
                if (!values[i].HasValue)
                    continue;

                var candidate = JsonSerializer.Deserialize<CachedPromptEntry>(values[i].ToString(), SerializerOptions);
                if (candidate?.Embedding is null)
                    continue;

                var score = CosineSimilarity(queryEmbedding, candidate.Embedding);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestEntry = candidate;
                }
            }

            if (bestEntry is not null && bestScore >= SemanticSimilarityThreshold)
            {
                bestEntry.HitCount++;
                bestEntry.LastHitAt = DateTime.UtcNow;
                var bestKey = $"{KeyPrefix}{bestEntry.CacheKey}";
                await db.StringSetAsync(bestKey, JsonSerializer.Serialize(bestEntry, SerializerOptions)).ConfigureAwait(false);
                await db.StringIncrementAsync(StatsHitsKey).ConfigureAwait(false);
                HitsCounter.Add(1);
                SemanticHitsCounter.Add(1);
                activity?.SetTag("cache.hit", true);
                activity?.SetTag("cache.match_type", "semantic");
                activity?.SetTag("cache.similarity_score", bestScore);

                return new CachedPromptResponse
                {
                    RoutingDecision = ToAgentChoiceResult(bestEntry),
                    IsExactMatch = false,
                    SimilarityScore = bestScore,
                    MatchedCacheKey = bestEntry.CacheKey
                };
            }

            await db.StringIncrementAsync(StatsMissesKey).ConfigureAwait(false);
            MissesCounter.Add(1);
            activity?.SetTag("cache.hit", false);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during prompt cache lookup");
            return null;
        }
        finally
        {
            sw.Stop();
            LookupDuration.Record(sw.Elapsed.TotalMilliseconds);
        }
    }

    public async Task CacheRoutingDecisionAsync(
        IList<ChatMessage> messages,
        AgentChoiceResult decision,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("PromptCache.Store");

        try
        {
            var db = _redis.GetDatabase();
            var normalizedPrompt = NormalizePrompt(messages);
            var hash = ComputeSha256(normalizedPrompt);
            var cacheKey = $"{KeyPrefix}{hash}";

            activity?.SetTag("cache.key", cacheKey);

            var embedding = await GenerateEmbeddingAsync(normalizedPrompt, cancellationToken).ConfigureAwait(false);

            var entry = new CachedPromptEntry
            {
                CacheKey = hash,
                NormalizedPrompt = normalizedPrompt,
                AgentId = decision.AgentId,
                Confidence = decision.Confidence,
                Reasoning = decision.Reasoning,
                AdditionalAgents = decision.AdditionalAgents,
                Embedding = embedding,
                HitCount = 0,
                CreatedAt = DateTime.UtcNow,
                LastHitAt = DateTime.UtcNow
            };

            await db.StringSetAsync(cacheKey, JsonSerializer.Serialize(entry, SerializerOptions)).ConfigureAwait(false);
            await db.SetAddAsync(IndexKey, cacheKey).ConfigureAwait(false);

            _logger.LogInformation("Cached routing decision for prompt key {CacheKey}: agent={AgentId}, confidence={Confidence:F2}",
                cacheKey, decision.AgentId, decision.Confidence);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error caching routing decision");
        }
    }

    // ── Chat response cache (agent-level) ───────────────────────────────

    public async Task<CachedChatResponseData?> TryGetCachedChatResponseAsync(
        string normalizedPrompt,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("ChatCache.Lookup");
        var sw = Stopwatch.StartNew();

        try
        {
            var db = _redis.GetDatabase();
            var hash = ComputeSha256(normalizedPrompt);
            var cacheKey = $"{ChatKeyPrefix}{hash}";

            activity?.SetTag("cache.key", cacheKey);

            // Exact match
            var exactValue = await db.StringGetAsync(cacheKey).ConfigureAwait(false);
            if (exactValue.HasValue)
            {
                var entry = JsonSerializer.Deserialize<CachedChatResponseData>(exactValue.ToString(), SerializerOptions);
                if (entry is not null && (entry.ResponseText is not null || entry.FunctionCalls is { Count: > 0 }))
                {
                    entry.HitCount++;
                    entry.LastHitAt = DateTime.UtcNow;
                    await db.StringSetAsync(cacheKey, JsonSerializer.Serialize(entry, SerializerOptions)).ConfigureAwait(false);
                    await db.StringIncrementAsync(ChatStatsHitsKey).ConfigureAwait(false);
                    ChatHitsCounter.Add(1);
                    activity?.SetTag("cache.hit", true);
                    activity?.SetTag("cache.match_type", "exact");

                    _logger.LogInformation(
                        "Chat cache hit (exact): key={CacheKey}, hasFunctionCalls={HasFunctionCalls}",
                        cacheKey, entry.FunctionCalls is { Count: > 0 });
                    return entry;
                }
            }

            // Semantic similarity fallback
            var indexMembers = await db.SetMembersAsync(ChatIndexKey).ConfigureAwait(false);
            if (indexMembers.Length == 0)
            {
                await db.StringIncrementAsync(ChatStatsMissesKey).ConfigureAwait(false);
                ChatMissesCounter.Add(1);
                activity?.SetTag("cache.hit", false);
                return null;
            }

            var queryEmbedding = await GenerateEmbeddingAsync(normalizedPrompt, cancellationToken).ConfigureAwait(false);
            if (queryEmbedding is null)
            {
                await db.StringIncrementAsync(ChatStatsMissesKey).ConfigureAwait(false);
                ChatMissesCounter.Add(1);
                return null;
            }

            CachedChatResponseData? bestEntry = null;
            double bestScore = 0;
            string? bestKey = null;

            var memberKeys = indexMembers.Select(m => (RedisKey)m.ToString()).ToArray();
            var values = await db.StringGetAsync(memberKeys).ConfigureAwait(false);

            for (var i = 0; i < values.Length; i++)
            {
                if (!values[i].HasValue)
                    continue;

                var candidate = JsonSerializer.Deserialize<CachedChatResponseData>(values[i].ToString(), SerializerOptions);
                if (candidate?.Embedding is null)
                    continue;

                var score = CosineSimilarity(queryEmbedding, candidate.Embedding);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestEntry = candidate;
                    bestKey = memberKeys[i].ToString();
                }
            }

            if (bestEntry is not null && bestScore >= SemanticSimilarityThreshold && bestKey is not null)
            {
                bestEntry.HitCount++;
                bestEntry.LastHitAt = DateTime.UtcNow;
                await db.StringSetAsync(bestKey, JsonSerializer.Serialize(bestEntry, SerializerOptions)).ConfigureAwait(false);
                await db.StringIncrementAsync(ChatStatsHitsKey).ConfigureAwait(false);
                ChatHitsCounter.Add(1);
                activity?.SetTag("cache.hit", true);
                activity?.SetTag("cache.match_type", "semantic");
                activity?.SetTag("cache.similarity_score", bestScore);

                _logger.LogInformation(
                    "Chat cache hit (semantic, score={Score:F3}): key={CacheKey}",
                    bestScore, bestKey);
                return bestEntry;
            }

            await db.StringIncrementAsync(ChatStatsMissesKey).ConfigureAwait(false);
            ChatMissesCounter.Add(1);
            activity?.SetTag("cache.hit", false);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during chat cache lookup");
            return null;
        }
        finally
        {
            sw.Stop();
            ChatLookupDuration.Record(sw.Elapsed.TotalMilliseconds);
        }
    }

    public async Task CacheChatResponseAsync(
        string normalizedPrompt,
        CachedChatResponseData data,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("ChatCache.Store");

        try
        {
            var db = _redis.GetDatabase();
            var hash = ComputeSha256(normalizedPrompt);
            var cacheKey = $"{ChatKeyPrefix}{hash}";

            activity?.SetTag("cache.key", cacheKey);

            var embedding = await GenerateEmbeddingAsync(normalizedPrompt, cancellationToken).ConfigureAwait(false);

            data.CacheKey = hash;
            // Preserve the clean display prompt if already set by the caller
            if (string.IsNullOrEmpty(data.NormalizedPrompt))
                data.NormalizedPrompt = normalizedPrompt;
            data.Embedding = embedding;
            data.CreatedAt = DateTime.UtcNow;
            data.LastHitAt = DateTime.UtcNow;

            await db.StringSetAsync(cacheKey, JsonSerializer.Serialize(data, SerializerOptions)).ConfigureAwait(false);
            await db.SetAddAsync(ChatIndexKey, cacheKey).ConfigureAwait(false);

            _logger.LogInformation(
                "Cached chat response for key {CacheKey}: hasText={HasText}, functionCalls={FunctionCallCount}",
                cacheKey,
                data.ResponseText is not null,
                data.FunctionCalls?.Count ?? 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error caching chat response");
        }
    }

    // ── Management ──────────────────────────────────────────────────────

    public async Task<List<CachedPromptEntry>> GetAllCachedEntriesAsync(CancellationToken cancellationToken = default)
    {
        var entries = new List<CachedPromptEntry>();

        try
        {
            var db = _redis.GetDatabase();
            var members = await db.SetMembersAsync(IndexKey).ConfigureAwait(false);

            if (members.Length > 0)
            {
                var memberKeys = members.Select(m => (RedisKey)m.ToString()).ToArray();
                var values = await db.StringGetAsync(memberKeys).ConfigureAwait(false);

                for (var i = 0; i < values.Length; i++)
                {
                    if (!values[i].HasValue)
                        continue;

                    var entry = JsonSerializer.Deserialize<CachedPromptEntry>(values[i].ToString(), SerializerOptions);
                    if (entry is not null)
                        entries.Add(entry);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving all cached entries");
        }

        return entries;
    }

    public async Task<bool> EvictAsync(string cacheKey, CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var redisKey = cacheKey.StartsWith(KeyPrefix, StringComparison.Ordinal)
                ? cacheKey
                : $"{KeyPrefix}{cacheKey}";

            var deleted = await db.KeyDeleteAsync(redisKey).ConfigureAwait(false);
            await db.SetRemoveAsync(IndexKey, redisKey).ConfigureAwait(false);
            return deleted;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error evicting cache entry {CacheKey}", cacheKey);
            return false;
        }
    }

    public async Task<long> EvictAllAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var members = await db.SetMembersAsync(IndexKey).ConfigureAwait(false);
            long count = 0;

            if (members.Length > 0)
            {
                var memberKeys = members.Select(m => (RedisKey)m.ToString()).ToArray();
                count = await db.KeyDeleteAsync(memberKeys).ConfigureAwait(false);
            }

            await db.KeyDeleteAsync(IndexKey).ConfigureAwait(false);
            await db.KeyDeleteAsync(StatsHitsKey).ConfigureAwait(false);
            await db.KeyDeleteAsync(StatsMissesKey).ConfigureAwait(false);

            return count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error evicting all cache entries");
            return 0;
        }
    }

    public async Task<PromptCacheStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var totalEntries = await db.SetLengthAsync(IndexKey).ConfigureAwait(false);
            var totalHits = (long)await db.StringGetAsync(StatsHitsKey).ConfigureAwait(false);
            var totalMisses = (long)await db.StringGetAsync(StatsMissesKey).ConfigureAwait(false);

            return new PromptCacheStats
            {
                TotalEntries = totalEntries,
                TotalHits = totalHits,
                TotalMisses = totalMisses
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving cache stats");
            return new PromptCacheStats();
        }
    }

    // ── Chat cache management ───────────────────────────────────────────

    public async Task<List<CachedChatResponseData>> GetAllChatCacheEntriesAsync(CancellationToken cancellationToken = default)
    {
        var entries = new List<CachedChatResponseData>();

        try
        {
            var db = _redis.GetDatabase();
            var members = await db.SetMembersAsync(ChatIndexKey).ConfigureAwait(false);

            if (members.Length > 0)
            {
                var memberKeys = members.Select(m => (RedisKey)m.ToString()).ToArray();
                var values = await db.StringGetAsync(memberKeys).ConfigureAwait(false);

                for (var i = 0; i < values.Length; i++)
                {
                    if (!values[i].HasValue)
                        continue;

                    var entry = JsonSerializer.Deserialize<CachedChatResponseData>(values[i].ToString(), SerializerOptions);
                    if (entry is not null)
                        entries.Add(entry);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving all chat cache entries");
        }

        return entries;
    }

    public async Task<bool> EvictChatEntryAsync(string cacheKey, CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var redisKey = cacheKey.StartsWith(ChatKeyPrefix, StringComparison.Ordinal)
                ? cacheKey
                : $"{ChatKeyPrefix}{cacheKey}";

            var deleted = await db.KeyDeleteAsync(redisKey).ConfigureAwait(false);
            await db.SetRemoveAsync(ChatIndexKey, redisKey).ConfigureAwait(false);
            return deleted;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error evicting chat cache entry {CacheKey}", cacheKey);
            return false;
        }
    }

    public async Task<long> EvictAllChatEntriesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var members = await db.SetMembersAsync(ChatIndexKey).ConfigureAwait(false);
            long count = 0;

            if (members.Length > 0)
            {
                var memberKeys = members.Select(m => (RedisKey)m.ToString()).ToArray();
                count = await db.KeyDeleteAsync(memberKeys).ConfigureAwait(false);
            }

            await db.KeyDeleteAsync(ChatIndexKey).ConfigureAwait(false);
            await db.KeyDeleteAsync(ChatStatsHitsKey).ConfigureAwait(false);
            await db.KeyDeleteAsync(ChatStatsMissesKey).ConfigureAwait(false);

            return count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error evicting all chat cache entries");
            return 0;
        }
    }

    public async Task<PromptCacheStats> GetChatCacheStatsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var totalEntries = await db.SetLengthAsync(ChatIndexKey).ConfigureAwait(false);
            var totalHits = (long)await db.StringGetAsync(ChatStatsHitsKey).ConfigureAwait(false);
            var totalMisses = (long)await db.StringGetAsync(ChatStatsMissesKey).ConfigureAwait(false);

            return new PromptCacheStats
            {
                TotalEntries = totalEntries,
                TotalHits = totalHits,
                TotalMisses = totalMisses
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving chat cache stats");
            return new PromptCacheStats();
        }
    }

    private static AgentChoiceResult ToAgentChoiceResult(CachedPromptEntry entry) => new()
    {
        AgentId = entry.AgentId,
        Confidence = entry.Confidence,
        Reasoning = entry.Reasoning ?? "[cached routing decision]",
        AdditionalAgents = entry.AdditionalAgents
    };

    internal static string NormalizePrompt(IList<ChatMessage> messages)
    {
        var sb = new StringBuilder();
        foreach (var message in messages.Where(m => m.Role == ChatRole.User))
        {
            var text = message.Text ?? string.Empty;
            sb.Append(text.ToLowerInvariant().Trim());
            sb.Append('\n');
        }

        var raw = sb.ToString();

        // Strip volatile HA context fields so identical intents hash the same
        raw = VolatileHaFieldsPattern.Replace(raw, string.Empty);

        sb.Clear();
        sb.EnsureCapacity(raw.Length);
        var previousWasSpace = false;
        foreach (var c in raw)
        {
            if (c != '\n' && char.IsWhiteSpace(c))
            {
                if (!previousWasSpace)
                    sb.Append(' ');
                previousWasSpace = true;
            }
            else
            {
                sb.Append(c);
                previousWasSpace = false;
            }
        }

        return sb.ToString();
    }

    private static string ComputeSha256(string input)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(hashBytes);
    }

    private async Task<float[]?> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken)
    {
        try
        {
            // Lazy-resolve the embedding generator on first use
            _embeddingGenerator ??= await _embeddingResolver.ResolveAsync(ct: cancellationToken).ConfigureAwait(false);
            if (_embeddingGenerator is null)
            {
                _logger.LogDebug("No embedding provider configured — prompt cache similarity search disabled.");
                return null;
            }

            var result = await _embeddingGenerator.GenerateAsync(text, cancellationToken: cancellationToken).ConfigureAwait(false);
            return result.Vector.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating embedding for prompt cache");
            return null;
        }
    }

    private static double CosineSimilarity(float[] vectorA, float[] vectorB)
    {
        if (vectorA.Length != vectorB.Length)
            return 0;

        double dotProduct = 0;
        double magnitudeA = 0;
        double magnitudeB = 0;

        for (var i = 0; i < vectorA.Length; i++)
        {
            dotProduct += vectorA[i] * (double)vectorB[i];
            magnitudeA += vectorA[i] * (double)vectorA[i];
            magnitudeB += vectorB[i] * (double)vectorB[i];
        }

        var magnitude = Math.Sqrt(magnitudeA) * Math.Sqrt(magnitudeB);
        return magnitude == 0 ? 0 : dotProduct / magnitude;
    }
}
