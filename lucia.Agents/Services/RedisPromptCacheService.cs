using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using lucia.Agents.Abstractions;
using lucia.Agents.Orchestration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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

    // Chat response cache entries are evicted via LRU TTL
    private const string ChatKeyPrefix = "lucia:chat-cache:";
    private const string ChatIndexKey = "lucia:chat-cache:index";
    private const string ChatStatsHitsKey = "lucia:chat-cache:stats:hits";
    private const string ChatStatsMissesKey = "lucia:chat-cache:stats:misses";

    private const int DefaultPromptCacheTtlHours = 48;

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
    private readonly TimeSpan _cacheTtl;
    private readonly IOptionsMonitor<RouterExecutorOptions> _routerOptions;
    private IEmbeddingSimilarityService _embeddingSimilarityService;

    public RedisPromptCacheService(
        IConnectionMultiplexer redis,
        IEmbeddingProviderResolver embeddingResolver,
        IConfiguration configuration,
        IEmbeddingSimilarityService embeddingSimilarityService,
        IOptionsMonitor<RouterExecutorOptions> routerOptions,
        ILogger<RedisPromptCacheService> logger)
    {
        _redis = redis;
        _embeddingResolver = embeddingResolver;
        _logger = logger;
        _embeddingSimilarityService = embeddingSimilarityService;
        _routerOptions = routerOptions;

        var ttlHours = configuration.GetValue("Redis:PromptCacheTtlHours", DefaultPromptCacheTtlHours);
        _cacheTtl = TimeSpan.FromHours(ttlHours);
        _logger.LogInformation(
            "Prompt cache LRU TTL configured to {TtlHours}h, routing threshold={RoutingThreshold:F2}, chat threshold={ChatThreshold:F2}",
            ttlHours, routerOptions.CurrentValue.SemanticSimilarityThreshold, routerOptions.CurrentValue.ChatCacheSemanticThreshold);
    }

    public async Task<CachedPromptResponse?> TryGetCachedRoutingDecisionAsync(
        IList<ChatMessage> messages,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity();
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
                    await db.StringSetAsync(cacheKey, JsonSerializer.Serialize(entry, SerializerOptions), _cacheTtl, flags: CommandFlags.FireAndForget).ConfigureAwait(false);
                    await db.StringIncrementAsync(StatsHitsKey, flags: CommandFlags.FireAndForget).ConfigureAwait(false);
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

            var queryEmbedding = await GenerateEmbeddingAsync(normalizedPrompt, cancellationToken)
                .ConfigureAwait(false);
            
            if (queryEmbedding is null)
            {
                await db.StringIncrementAsync(StatsMissesKey, flags: CommandFlags.FireAndForget)
                    .ConfigureAwait(false);
                MissesCounter.Add(1);
                return null;
            }

            CachedPromptEntry? bestEntry = null;
            double bestScore = 0;

            var memberKeys = indexMembers.Select(m => (RedisKey)m.ToString()).ToArray();
            var values = await db.StringGetAsync(memberKeys).ConfigureAwait(false);

            // Clean expired keys from the index set while iterating
            var expiredMembers = new List<RedisValue>();

            for (var i = 0; i < values.Length; i++)
            {
                if (!values[i].HasValue)
                {
                    expiredMembers.Add(indexMembers[i]);
                    continue;
                }

                var candidate = JsonSerializer.Deserialize<CachedPromptEntry>(values[i].ToString(), SerializerOptions);
                if (candidate?.Embedding is null)
                    continue;
                var candidateEmbedding = new Embedding<float>(candidate.Embedding);
                var score = _embeddingSimilarityService.ComputeSimilarity(queryEmbedding, candidateEmbedding);
                if (!(score > bestScore)) continue;
                bestScore = score;
                bestEntry = candidate;
            }

            // Remove stale index entries whose Redis keys have expired
            if (expiredMembers.Count > 0)
            {
                await db.SetRemoveAsync(IndexKey, [.. expiredMembers]).ConfigureAwait(false);
                _logger.LogDebug("Cleaned {Count} expired routing cache entries from index", expiredMembers.Count);
            }

            var routingThreshold = _routerOptions.CurrentValue.SemanticSimilarityThreshold;
            if (bestEntry is not null && bestScore >= routingThreshold)
            {
                bestEntry.HitCount++;
                bestEntry.LastHitAt = DateTime.UtcNow;
                var bestKey = $"{KeyPrefix}{bestEntry.CacheKey}";
                await db.StringSetAsync(bestKey, JsonSerializer.Serialize(bestEntry, SerializerOptions)).ConfigureAwait(false);
                await db.KeyExpireAsync(bestKey, _cacheTtl).ConfigureAwait(false);
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

            // Log why the semantic fallback didn't produce a hit
            if (bestEntry is not null)
            {
                _logger.LogDebug(
                    "Routing cache semantic miss: bestScore={BestScore:F4} < threshold={Threshold:F4}, " +
                    "query=\"{Query}\", bestMatch=\"{BestMatch}\", candidates={CandidateCount}",
                    bestScore, routingThreshold, normalizedPrompt,
                    bestEntry.NormalizedPrompt, indexMembers.Length);
                activity?.SetTag("cache.miss_reason", "below_threshold");
                activity?.SetTag("cache.best_score", bestScore);
            }
            else
            {
                _logger.LogDebug(
                    "Routing cache semantic miss: no candidates with embeddings, query=\"{Query}\", indexSize={IndexSize}",
                    normalizedPrompt, indexMembers.Length);
                activity?.SetTag("cache.miss_reason", "no_candidates_with_embeddings");
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
        using var activity = ActivitySource.StartActivity();

        try
        {
            var db = _redis.GetDatabase();
            var normalizedPrompt = NormalizePrompt(messages);
            var hash = ComputeSha256(normalizedPrompt);
            var cacheKey = $"{KeyPrefix}{hash}";

            activity?.SetTag("cache.key", cacheKey);

            var embedding = await GenerateEmbeddingAsync(normalizedPrompt, cancellationToken)
                .ConfigureAwait(false);

            var entry = new CachedPromptEntry
            {
                CacheKey = cacheKey,
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

            await db.StringSetAsync(cacheKey, JsonSerializer.Serialize(entry, SerializerOptions)).ConfigureAwait(false);
            await db.KeyExpireAsync(cacheKey, _cacheTtl, flags: CommandFlags.FireAndForget).ConfigureAwait(false);
            await db.SetAddAsync(IndexKey, cacheKey, flags: CommandFlags.FireAndForget).ConfigureAwait(false);

            _logger.LogInformation(
                "Cached routing decision for prompt key {CacheKey}: agent={AgentId}, confidence={Confidence:F2}, hasEmbedding={HasEmbedding}",
                cacheKey, decision.AgentId, decision.Confidence, embedding is not null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error caching routing decision");
        }
    }

    // ── Chat response cache (agent-level) ───────────────────────────────

    public async Task<CachedChatResponseData?> TryGetCachedChatResponseAsync(
        string normalizedPrompt,
        string? semanticQueryText = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity();
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
                    await db.KeyExpireAsync(cacheKey, _cacheTtl).ConfigureAwait(false);
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

            // Semantic similarity fallback — embed only the user text (not the full
            // key which includes the system prompt). Stored embeddings use the same
            // user-text-only approach via CacheChatResponseAsync.
            var indexMembers = await db.SetMembersAsync(ChatIndexKey).ConfigureAwait(false);
            if (indexMembers.Length == 0)
            {
                await db.StringIncrementAsync(ChatStatsMissesKey).ConfigureAwait(false);
                ChatMissesCounter.Add(1);
                activity?.SetTag("cache.hit", false);
                return null;
            }

            var embeddingInput = semanticQueryText ?? normalizedPrompt;
            var queryEmbedding = await GenerateEmbeddingAsync(embeddingInput, cancellationToken).ConfigureAwait(false);
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

            // Clean expired keys from the index set while iterating
            var expiredMembers = new List<RedisValue>();

            for (var i = 0; i < values.Length; i++)
            {
                if (!values[i].HasValue)
                {
                    expiredMembers.Add(indexMembers[i]);
                    continue;
                }

                var candidate = JsonSerializer.Deserialize<CachedChatResponseData>(values[i].ToString(), SerializerOptions);
                if (candidate?.Embedding is null)
                    continue;
                var candidateEmbedding = new Embedding<float>(candidate.Embedding);
                var score = _embeddingSimilarityService.ComputeSimilarity(queryEmbedding, candidateEmbedding);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestEntry = candidate;
                    bestKey = memberKeys[i].ToString();
                }
            }

            // Remove stale index entries whose Redis keys have expired
            if (expiredMembers.Count > 0)
            {
                await db.SetRemoveAsync(ChatIndexKey, [.. expiredMembers]).ConfigureAwait(false);
                _logger.LogDebug("Cleaned {Count} expired chat cache entries from index", expiredMembers.Count);
            }

            var chatThreshold = _routerOptions.CurrentValue.ChatCacheSemanticThreshold;
            if (bestEntry is not null && bestScore >= chatThreshold && bestKey is not null)
            {
                bestEntry.HitCount++;
                bestEntry.LastHitAt = DateTime.UtcNow;
                await db.StringSetAsync(bestKey, JsonSerializer.Serialize(bestEntry, SerializerOptions)).ConfigureAwait(false);
                await db.KeyExpireAsync(bestKey, _cacheTtl).ConfigureAwait(false);
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

            // Log why the semantic fallback didn't produce a hit
            if (bestEntry is not null)
            {
                _logger.LogDebug(
                    "Chat cache semantic miss: bestScore={BestScore:F4} < threshold={Threshold:F4}, " +
                    "query=\"{Query}\", bestMatch=\"{BestMatch}\", candidates={CandidateCount}",
                    bestScore, chatThreshold, embeddingInput,
                    bestEntry.NormalizedPrompt, indexMembers.Length);
                activity?.SetTag("cache.miss_reason", "below_threshold");
                activity?.SetTag("cache.best_score", bestScore);
            }
            else
            {
                _logger.LogDebug(
                    "Chat cache semantic miss: no candidates with embeddings, query=\"{Query}\", indexSize={IndexSize}",
                    embeddingInput, indexMembers.Length);
                activity?.SetTag("cache.miss_reason", "no_candidates_with_embeddings");
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
        using var activity = ActivitySource.StartActivity();

        try
        {
            var db = _redis.GetDatabase();
            var hash = ComputeSha256(normalizedPrompt);
            var cacheKey = $"{ChatKeyPrefix}{hash}";

            activity?.SetTag("cache.key", cacheKey);

            data.CacheKey = hash;
            // Preserve the clean display prompt if already set by the caller
            if (string.IsNullOrEmpty(data.NormalizedPrompt))
                data.NormalizedPrompt = normalizedPrompt;

            // Embed only the user text (NormalizedPrompt), NOT the full key which
            // includes the system prompt. The system prompt dominates the embedding
            // and makes all entries look identical, defeating semantic similarity.
            var embeddingText = data.NormalizedPrompt;
            var embedding = await GenerateEmbeddingAsync(embeddingText, cancellationToken).ConfigureAwait(false);
            data.Embedding = embedding?.Vector.ToArray();
            data.CreatedAt = DateTime.UtcNow;
            data.LastHitAt = DateTime.UtcNow;

            await db.StringSetAsync(cacheKey, JsonSerializer.Serialize(data, SerializerOptions)).ConfigureAwait(false);
            await db.KeyExpireAsync(cacheKey, _cacheTtl).ConfigureAwait(false);
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

                foreach (var redisValue in values)
                {
                    if (!redisValue.HasValue)
                        continue;

                    var entry = JsonSerializer.Deserialize<CachedPromptEntry>(redisValue.ToString(), SerializerOptions);
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

    private static string NormalizePrompt(IList<ChatMessage> messages)
    {
        // get the last user message
        var userMessage = messages.LastOrDefault(m => m.Role == ChatRole.User);
        
        // No user message in chat history. Shouldn't happen, but let the cache system deal with it
        if (userMessage is null)
            return string.Empty;
        
        // Pull the last line, as prompts can be prefixed with HA data that we don't care about
        var promptLineArray = userMessage.Text.Split(["\r\n", "\n"], StringSplitOptions.None);
        // walk the prompt backwards to find the last line
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
            // Lazy-resolve the embedding generator on first use
            _embeddingGenerator ??= await _embeddingResolver.ResolveAsync(ct: cancellationToken).ConfigureAwait(false);
            if (_embeddingGenerator is null)
            {
                _logger.LogWarning("No embedding provider configured — prompt cache similarity search disabled.");
                return null;
            }

            var result = await _embeddingGenerator.GenerateAsync(text, cancellationToken: cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating embedding for prompt cache");
            return null;
        }
    }
}
