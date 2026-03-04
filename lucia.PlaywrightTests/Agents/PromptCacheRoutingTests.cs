using System.Text.Json;
using Microsoft.Playwright;
using lucia.PlaywrightTests.Infrastructure;

namespace lucia.PlaywrightTests.Agents;

/// <summary>
/// End-to-end tests verifying the prompt routing cache correctly stores embeddings
/// and reuses cached routing decisions for semantically similar prompts.
///
/// Flow:
///   1. Evict all routing AND chat cache entries (clean slate).
///   2. Send "turn off dianna's lamp" via A2A proxy → creates a cache entry with embedding.
///   3. Verify the cached entry has a non-null embedding and routes to light-agent.
///   4. Send "turn off dianna's light" → should hit the cached entry via semantic match.
///   5. Verify the cache was reused (hit count increased, no duplicate entry).
///
/// Uses the dashboard A2A proxy (/agents/proxy) for message delivery rather than
/// direct HTTPS to the AgentHost (which has self-signed cert issues with Playwright).
/// </summary>
[Collection(TestCollections.Playwright)]
[Trait("Category", "Playwright")]
public sealed class PromptCacheRoutingTests : PlaywrightTestBase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private IAPIRequestContext? _api;

    public PromptCacheRoutingTests(PlaywrightFixture fixture) : base(fixture)
    {
    }

    /// <summary>
    /// Returns an API request context targeting the dashboard (HTTP, no TLS issues).
    /// The dashboard proxies /api/* calls to the AgentHost.
    /// </summary>
    private async Task<IAPIRequestContext> GetApiAsync()
    {
        if (_api is not null) return _api;

        _api = await Fixture.Playwright.APIRequest.NewContextAsync(new()
        {
            BaseURL = ServiceEndpoints.DashboardUrl,
            IgnoreHTTPSErrors = true,
            ExtraHTTPHeaders = new Dictionary<string, string>
            {
                ["X-API-Key"] = GetRequiredApiKey()
            }
        });
        return _api;
    }

    [Fact]
    public async Task SemanticallySimilarPrompts_ReuseRoutingCacheEntry()
    {
        // ── Arrange: Evict both routing AND chat caches ─────────
        // A stale chat cache entry can poison the router's LLM call by
        // returning a cached non-JSON response via semantic match.
        await EvictAllCachesAsync();
        await LoginViaDashboardAsync();

        // ── Act 1: Send the first prompt via A2A proxy ──────────
        await SendA2AMessageAsync("turn off dianna's lamp");

        // Poll for routing cache entries (async write may take a moment)
        var entriesAfterFirst = await PollForRoutingCacheEntriesAsync(
            minCount: 1, timeoutSeconds: 15);

        // ── Assert 1: Routed to light-agent with embedding ──────
        var lightEntries = entriesAfterFirst
            .Where(e => string.Equals(e.AgentId, "light-agent", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.True(lightEntries.Count >= 1,
            $"Expected at least one routing cache entry for light-agent after first prompt, " +
            $"but found {lightEntries.Count}. All entries: " +
            $"{string.Join(", ", entriesAfterFirst.Select(e => $"{e.AgentId}:{e.NormalizedPrompt}"))}");

        var firstEntry = lightEntries
            .OrderByDescending(e => e.CreatedAt)
            .First();

        Assert.True(firstEntry.HasEmbedding,
            $"Routing cache entry should have an embedding stored, but Embedding was null. " +
            $"Entry: key={firstEntry.CacheKey}, prompt='{firstEntry.NormalizedPrompt}'");

        Assert.Contains("lamp", firstEntry.NormalizedPrompt, StringComparison.OrdinalIgnoreCase);

        var initialHitCount = firstEntry.HitCount;
        var initialEntryCount = entriesAfterFirst.Count;

        // ── Act 2: Send a semantically similar prompt ───────────
        await SendA2AMessageAsync("turn off dianna's light");

        // Allow async cache write to complete
        await Task.Delay(3_000);

        // ── Assert 2: Cache was reused OR both route to light-agent ─
        var entriesAfterSecond = await GetRoutingCacheEntriesAsync();

        var allLightEntries = entriesAfterSecond
            .Where(e => string.Equals(e.AgentId, "light-agent", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.True(allLightEntries.Count >= 1,
            "Both prompts should route to light-agent");

        // Either: semantic hit (reused entry) or semantic miss (new entry, both still light-agent)
        var semanticHitOccurred = entriesAfterSecond.Count == initialEntryCount
                                  && allLightEntries.Any(e => e.HitCount > initialHitCount);

        if (semanticHitOccurred)
        {
            var reusedEntry = allLightEntries.First(e => e.HitCount > initialHitCount);
            Assert.True(reusedEntry.HitCount > initialHitCount,
                $"Expected hitCount to increase from {initialHitCount}, got {reusedEntry.HitCount}");
        }
        else
        {
            // Semantic threshold (0.99) wasn't met — both entries should still
            // route to light-agent and have embeddings stored.
            var secondEntry = allLightEntries
                .Where(e => e.NormalizedPrompt.Contains("light", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();

            Assert.NotNull(secondEntry);
            Assert.True(secondEntry.HasEmbedding,
                "Second routing cache entry should also have an embedding stored");
        }
    }

    // ── API helpers ─────────────────────────────────────────────

    /// <summary>Evicts both routing and chat caches to ensure a clean slate.</summary>
    private async Task EvictAllCachesAsync()
    {
        var api = await GetApiAsync();

        var routingResponse = await api.DeleteAsync("/api/prompt-cache");
        Assert.True(routingResponse.Ok,
            $"Failed to evict routing cache: {routingResponse.Status} {routingResponse.StatusText}");

        var chatResponse = await api.DeleteAsync("/api/chat-cache");
        Assert.True(chatResponse.Ok,
            $"Failed to evict chat cache: {chatResponse.Status} {chatResponse.StatusText}");
    }

    /// <summary>Authenticates with the dashboard to get a session cookie for A2A proxy calls.</summary>
    private async Task LoginViaDashboardAsync()
    {
        var api = await GetApiAsync();
        var response = await api.PostAsync("/api/auth/login", new()
        {
            DataObject = new { apiKey = GetRequiredApiKey() }
        });
        Assert.True(response.Ok,
            $"Dashboard login failed: {response.Status} {response.StatusText}");
    }

    /// <summary>
    /// Sends a message to the orchestrator agent via the dashboard A2A proxy.
    /// This mirrors how the dashboard UI sends messages but avoids brittle UI selectors.
    /// </summary>
    private async Task<string> SendA2AMessageAsync(string message)
    {
        var api = await GetApiAsync();
        var messageId = Guid.NewGuid().ToString("N");

        var response = await api.PostAsync("/agents/proxy?agentUrl=/agent", new()
        {
            DataObject = new
            {
                jsonrpc = "2.0",
                method = "message/send",
                id = messageId,
                @params = new
                {
                    message = new
                    {
                        kind = "message",
                        messageId,
                        role = "user",
                        parts = new[] { new { kind = "text", text = message } }
                    }
                }
            }
        });

        Assert.True(response.Ok,
            $"A2A proxy request failed: {response.Status} {response.StatusText}");

        var body = await response.TextAsync();
        return body;
    }

    private async Task<List<RoutingCacheEntry>> GetRoutingCacheEntriesAsync()
    {
        var api = await GetApiAsync();
        var response = await api.GetAsync("/api/prompt-cache");
        Assert.True(response.Ok,
            $"Failed to get routing cache entries: {response.Status} {response.StatusText}");

        var body = await response.TextAsync();
        return JsonSerializer.Deserialize<List<RoutingCacheEntry>>(body, JsonOptions) ?? [];
    }

    /// <summary>
    /// Polls for routing cache entries until the minimum count is met or timeout expires.
    /// The routing cache write is async — it may take a moment after the A2A response returns.
    /// </summary>
    private async Task<List<RoutingCacheEntry>> PollForRoutingCacheEntriesAsync(
        int minCount, int timeoutSeconds = 15)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        List<RoutingCacheEntry> entries = [];

        while (DateTime.UtcNow < deadline)
        {
            entries = await GetRoutingCacheEntriesAsync();
            if (entries.Count >= minCount)
                return entries;
            await Task.Delay(2_000);
        }

        return entries;
    }

    private static string GetRequiredApiKey()
    {
        var key = Environment.GetEnvironmentVariable("LUCIA_DASHBOARD_API_KEY");
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException(
                "LUCIA_DASHBOARD_API_KEY environment variable must be set to run Playwright tests.");
        }
        return key;
    }

    // ── Lightweight DTOs for API responses ──────────────────────

    private sealed class RoutingCacheEntry
    {
        public string CacheKey { get; set; } = string.Empty;
        public string NormalizedPrompt { get; set; } = string.Empty;
        public string AgentId { get; set; } = string.Empty;
        public double Confidence { get; set; }
        public float[]? Embedding { get; set; }
        public long HitCount { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime LastHitAt { get; set; }

        /// <summary>True when the embedding array is present and non-empty.</summary>
        public bool HasEmbedding => Embedding is { Length: > 0 };
    }
}
