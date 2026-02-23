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
        var group = endpoints.MapGroup("/api/prompt-cache")
            .WithTags("PromptCache")
            .RequireAuthorization();

        group.MapGet("/", GetAllEntriesAsync);

        group.MapGet("/stats", GetStatsAsync);

        group.MapDelete("/entry/{cacheKey}", EvictEntryAsync);

        group.MapDelete("/", EvictAllAsync);

        return endpoints;
    }

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
}
