using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

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
    private const string KeyPrefix = "lucia:prompt-cache:";
    private const string IndexKey = "lucia:prompt-cache:index";
    private const string StatsHitsKey = "lucia:prompt-cache:stats:hits";
    private const string StatsMissesKey = "lucia:prompt-cache:stats:misses";
    // High threshold required — home automation prompts differ by a single noun
    // (e.g., "kitchen lights" vs "office lights" score ~0.95 with embeddings)
    private const double SemanticSimilarityThreshold = 0.99;

    private static readonly ActivitySource ActivitySource = new("Lucia.Services.PromptCache", "1.0.0");
    private static readonly Meter Meter = new("Lucia.Services.PromptCache", "1.0.0");

    private static readonly Counter<long> HitsCounter = Meter.CreateCounter<long>("prompt.cache.hits");
    private static readonly Counter<long> MissesCounter = Meter.CreateCounter<long>("prompt.cache.misses");
    private static readonly Counter<long> SemanticHitsCounter = Meter.CreateCounter<long>("prompt.cache.semantic_hits");
    private static readonly Histogram<double> LookupDuration = Meter.CreateHistogram<double>("prompt.cache.lookup.duration", "ms");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IConnectionMultiplexer _redis;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly ILogger<RedisPromptCacheService> _logger;

    public RedisPromptCacheService(
        IConnectionMultiplexer redis,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        ILogger<RedisPromptCacheService> logger)
    {
        _redis = redis;
        _embeddingGenerator = embeddingGenerator;
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
            var exactValue = await db.StringGetAsync(cacheKey);
            if (exactValue.HasValue)
            {
                var entry = JsonSerializer.Deserialize<CachedPromptEntry>(exactValue.ToString(), SerializerOptions);
                if (entry is not null && !string.IsNullOrWhiteSpace(entry.AgentId))
                {
                    entry.HitCount++;
                    entry.LastHitAt = DateTime.UtcNow;
                    await db.StringSetAsync(cacheKey, JsonSerializer.Serialize(entry, SerializerOptions));
                    await db.StringIncrementAsync(StatsHitsKey);
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
            var indexMembers = await db.SetMembersAsync(IndexKey);
            if (indexMembers.Length == 0)
            {
                await db.StringIncrementAsync(StatsMissesKey);
                MissesCounter.Add(1);
                activity?.SetTag("cache.hit", false);
                return null;
            }

            var queryEmbedding = await GenerateEmbeddingAsync(normalizedPrompt, cancellationToken);
            if (queryEmbedding is null)
            {
                await db.StringIncrementAsync(StatsMissesKey);
                MissesCounter.Add(1);
                return null;
            }

            CachedPromptEntry? bestEntry = null;
            double bestScore = 0;

            foreach (var member in indexMembers)
            {
                var entryJson = await db.StringGetAsync(member.ToString());
                if (!entryJson.HasValue)
                    continue;

                var candidate = JsonSerializer.Deserialize<CachedPromptEntry>(entryJson.ToString(), SerializerOptions);
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
                await db.StringSetAsync(bestKey, JsonSerializer.Serialize(bestEntry, SerializerOptions));
                await db.StringIncrementAsync(StatsHitsKey);
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

            await db.StringIncrementAsync(StatsMissesKey);
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

            var embedding = await GenerateEmbeddingAsync(normalizedPrompt, cancellationToken);

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

            await db.StringSetAsync(cacheKey, JsonSerializer.Serialize(entry, SerializerOptions));
            await db.SetAddAsync(IndexKey, cacheKey);

            _logger.LogInformation("Cached routing decision for prompt key {CacheKey}: agent={AgentId}, confidence={Confidence:F2}",
                cacheKey, decision.AgentId, decision.Confidence);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error caching routing decision");
        }
    }

    public async Task<List<CachedPromptEntry>> GetAllCachedEntriesAsync(CancellationToken cancellationToken = default)
    {
        var entries = new List<CachedPromptEntry>();

        try
        {
            var db = _redis.GetDatabase();
            var members = await db.SetMembersAsync(IndexKey);

            foreach (var member in members)
            {
                var json = await db.StringGetAsync(member.ToString());
                if (!json.HasValue)
                    continue;

                var entry = JsonSerializer.Deserialize<CachedPromptEntry>(json.ToString(), SerializerOptions);
                if (entry is not null)
                    entries.Add(entry);
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

            var deleted = await db.KeyDeleteAsync(redisKey);
            await db.SetRemoveAsync(IndexKey, redisKey);
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
            var members = await db.SetMembersAsync(IndexKey);
            long count = 0;

            foreach (var member in members)
            {
                if (await db.KeyDeleteAsync(member.ToString()))
                    count++;
            }

            await db.KeyDeleteAsync(IndexKey);
            await db.KeyDeleteAsync(StatsHitsKey);
            await db.KeyDeleteAsync(StatsMissesKey);

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
            var totalEntries = await db.SetLengthAsync(IndexKey);
            var totalHits = (long)await db.StringGetAsync(StatsHitsKey);
            var totalMisses = (long)await db.StringGetAsync(StatsMissesKey);

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

    private static AgentChoiceResult ToAgentChoiceResult(CachedPromptEntry entry) => new()
    {
        AgentId = entry.AgentId,
        Confidence = entry.Confidence,
        Reasoning = entry.Reasoning ?? "[cached routing decision]",
        AdditionalAgents = entry.AdditionalAgents
    };

    private static string NormalizePrompt(IList<ChatMessage> messages)
    {
        var sb = new StringBuilder();
        foreach (var message in messages.Where(m => m.Role == ChatRole.User))
        {
            var text = message.Text ?? string.Empty;
            sb.Append(text.ToLowerInvariant().Trim());
            sb.Append('\n');
        }

        var raw = sb.ToString();
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
            var result = await _embeddingGenerator.GenerateAsync(text, cancellationToken: cancellationToken);
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
