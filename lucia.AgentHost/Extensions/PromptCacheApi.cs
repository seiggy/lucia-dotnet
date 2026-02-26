using lucia.Agents.Models;
using lucia.Agents.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace lucia.AgentHost.Extensions;

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

        routing.MapGet("/", GetAllEntriesAsync);
        routing.MapGet("/stats", GetStatsAsync);
        routing.MapDelete("/entry/{cacheKey}", EvictEntryAsync);
        routing.MapDelete("/", EvictAllAsync);

        // Agent-level chat cache
        var chat = endpoints.MapGroup("/api/chat-cache")
            .WithTags("ChatCache")
            .RequireAuthorization();

        chat.MapGet("/", GetAllChatEntriesAsync);
        chat.MapGet("/stats", GetChatStatsAsync);
        chat.MapDelete("/entry/{cacheKey}", EvictChatEntryAsync);
        chat.MapDelete("/", EvictAllChatEntriesAsync);

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
