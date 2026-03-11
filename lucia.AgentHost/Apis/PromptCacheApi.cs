using lucia.Agents.Abstractions;
using lucia.Agents.Models;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace lucia.AgentHost.Apis;

/// <summary>
/// Minimal API endpoints for managing the prompt cache.
/// </summary>
public static class PromptCacheApi
{
    public static IEndpointRouteBuilder MapPromptCacheApi(this IEndpointRouteBuilder endpoints)
    {
        // Routing cache
        var routing = endpoints.MapGroup("/api/prompt-cache")
            .WithTags("PromptCache")
            .RequireAuthorization();

        routing.MapGet("/", GetAllEntriesAsync)
            .WithSummary("List routing cache entries")
            .WithDescription("Returns all cached routing decisions for the management UI.");
        routing.MapGet("/stats", GetStatsAsync)
            .WithSummary("Get routing cache statistics")
            .WithDescription("Returns total entries, hit count, miss count, and hit rate.");
        routing.MapDelete("/entry/{cacheKey}", EvictEntryAsync)
            .WithSummary("Evict a routing cache entry");
        routing.MapDelete("/", EvictAllAsync)
            .WithSummary("Clear all routing cache entries");

        // Agent-level chat cache
        var chat = endpoints.MapGroup("/api/chat-cache")
            .WithTags("ChatCache")
            .RequireAuthorization();

        chat.MapGet("/", GetAllChatEntriesAsync)
            .WithSummary("List chat cache entries")
            .WithDescription("Returns all cached agent-level LLM responses.");
        chat.MapGet("/stats", GetChatStatsAsync)
            .WithSummary("Get chat cache statistics");
        chat.MapDelete("/entry/{cacheKey}", EvictChatEntryAsync)
            .WithSummary("Evict a chat cache entry");
        chat.MapDelete("/", EvictAllChatEntriesAsync)
            .WithSummary("Clear all chat cache entries");

        return endpoints;
    }

    // ── Routing cache handlers ──────────────────────────────────────────

    private static async Task<Ok<List<CachedPromptEntry>>> GetAllEntriesAsync(
        [FromServices] IPromptCacheService cacheService,
        CancellationToken ct)
    {
        var entries = await cacheService.GetAllCachedEntriesAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(entries);
    }

    private static async Task<Ok<PromptCacheStats>> GetStatsAsync(
        [FromServices] IPromptCacheService cacheService,
        CancellationToken ct)
    {
        var stats = await cacheService.GetStatsAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(stats);
    }

    private static async Task<Ok<object>> EvictEntryAsync(
        [FromServices] IPromptCacheService cacheService,
        [FromRoute] string cacheKey,
        CancellationToken ct)
    {
        var found = await cacheService.EvictAsync(cacheKey, ct).ConfigureAwait(false);
        return TypedResults.Ok<object>(new { evicted = found });
    }

    private static async Task<Ok<object>> EvictAllAsync(
        [FromServices] IPromptCacheService cacheService,
        CancellationToken ct)
    {
        var count = await cacheService.EvictAllAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok<object>(new { evicted = count });
    }

    // ── Chat cache handlers ─────────────────────────────────────────────

    private static async Task<Ok<List<CachedChatResponseData>>> GetAllChatEntriesAsync(
        [FromServices] IPromptCacheService cacheService,
        CancellationToken ct)
    {
        var entries = await cacheService.GetAllChatCacheEntriesAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(entries);
    }

    private static async Task<Ok<PromptCacheStats>> GetChatStatsAsync(
        [FromServices] IPromptCacheService cacheService,
        CancellationToken ct)
    {
        var stats = await cacheService.GetChatCacheStatsAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(stats);
    }

    private static async Task<Ok<object>> EvictChatEntryAsync(
        [FromServices] IPromptCacheService cacheService,
        [FromRoute] string cacheKey,
        CancellationToken ct)
    {
        var found = await cacheService.EvictChatEntryAsync(cacheKey, ct).ConfigureAwait(false);
        return TypedResults.Ok<object>(new { evicted = found });
    }

    private static async Task<Ok<object>> EvictAllChatEntriesAsync(
        [FromServices] IPromptCacheService cacheService,
        CancellationToken ct)
    {
        var count = await cacheService.EvictAllChatEntriesAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok<object>(new { evicted = count });
    }
}
