using System.Text.Json;
using lucia.Agents.Abstractions;
using lucia.Agents.Models.HomeAssistant;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace lucia.AgentHost.Apis;

/// <summary>
/// Minimal API endpoints for viewing and managing the entity location cache.
/// </summary>
public static class EntityLocationCacheApi
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly TimeSpan EmbeddingProgressSseInterval = TimeSpan.FromMilliseconds(750);

    public static IEndpointRouteBuilder MapEntityLocationCacheApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/entity-location")
            .WithTags("EntityLocation")
            .RequireAuthorization();

        group.MapGet("/", GetSummaryAsync);
        group.MapGet("/floors", GetFloorsAsync);
        group.MapGet("/areas", GetAreasAsync);
        group.MapGet("/entities", GetEntitiesAsync);
        group.MapGet("/search/{term}", SearchAsync);
        group.MapGet("/embedding-progress", GetEmbeddingProgressAsync);
        group.MapGet("/embedding-progress/live", StreamEmbeddingProgressAsync);
        group.MapPost("/invalidate", InvalidateAsync);
        group.MapDelete("/embeddings/{itemType}/{itemId}", EvictEmbeddingAsync);
        group.MapPost("/embeddings/{itemType}/{itemId}/regenerate", RegenerateEmbeddingAsync);

        return endpoints;
    }

    private static async Task<Ok<object>> GetSummaryAsync(
        [FromServices] IEntityLocationService locationService,
        CancellationToken ct)
    {
        var floors = await locationService.GetFloorsAsync(ct).ConfigureAwait(false);
        var areas = await locationService.GetAreasAsync(ct).ConfigureAwait(false);
        var entities = await locationService.GetEntitiesAsync(ct).ConfigureAwait(false);
        var embeddingProgress = await locationService.GetEmbeddingProgressAsync(ct).ConfigureAwait(false);

        return TypedResults.Ok<object>(new
        {
            floorCount = floors.Count,
            areaCount = areas.Count,
            entityCount = entities.Count,
            floorEmbeddingsGenerated = embeddingProgress.FloorGeneratedCount,
            areaEmbeddingsGenerated = embeddingProgress.AreaGeneratedCount,
            entityEmbeddingsGenerated = embeddingProgress.EntityGeneratedCount,
            entityEmbeddingsMissing = embeddingProgress.EntityMissingCount,
            embeddingGenerationInProgress = embeddingProgress.IsGenerationRunning,
            lastLoadedAt = locationService.LastLoadedAt?.ToString("O")
        });
    }

    private static async Task<Ok<EntityLocationEmbeddingProgress>> GetEmbeddingProgressAsync(
        [FromServices] IEntityLocationService locationService,
        CancellationToken ct)
    {
        var progress = await locationService.GetEmbeddingProgressAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(progress);
    }

    private static async Task StreamEmbeddingProgressAsync(
        [FromServices] IEntityLocationService locationService,
        HttpContext ctx,
        CancellationToken ct)
    {
        ctx.Response.ContentType = "text/event-stream";
        ctx.Response.Headers.CacheControl = "no-cache";
        ctx.Response.Headers.Connection = "keep-alive";

        string? previousPayload = null;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var progress = await locationService.GetEmbeddingProgressAsync(ct).ConfigureAwait(false);
                var payload = JsonSerializer.Serialize(progress, JsonOptions);
                if (!string.Equals(payload, previousPayload, StringComparison.Ordinal))
                {
                    await ctx.Response.WriteAsync($"data: {payload}\n\n", ct).ConfigureAwait(false);
                    await ctx.Response.Body.FlushAsync(ct).ConfigureAwait(false);
                    previousPayload = payload;
                }

                await Task.Delay(EmbeddingProgressSseInterval, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Client disconnected.
        }
    }

    private static async Task<Ok<object>> GetFloorsAsync(
        [FromServices] IEntityLocationService locationService,
        CancellationToken ct)
    {
        var floors = await locationService.GetFloorsAsync(ct).ConfigureAwait(false);
        var areas = await locationService.GetAreasAsync(ct).ConfigureAwait(false);

        var result = floors.Select(f => new
        {
            f.FloorId,
            f.Name,
            f.Aliases,
            f.Level,
            f.Icon,
            embeddingGenerated = f.NameEmbedding is not null,
            AreaCount = areas.Count(a => a.FloorId == f.FloorId),
            Areas = areas.Where(a => a.FloorId == f.FloorId).Select(a => a.Name)
        });

        return TypedResults.Ok<object>(result);
    }

    private static async Task<Ok<object>> GetAreasAsync(
        [FromServices] IEntityLocationService locationService,
        CancellationToken ct)
    {
        var areas = await locationService.GetAreasAsync(ct).ConfigureAwait(false);

        var result = areas.Select(a => new
        {
            a.AreaId,
            a.Name,
            a.FloorId,
            a.Aliases,
            a.Icon,
            a.Labels,
            embeddingGenerated = a.NameEmbedding is not null,
            EntityCount = a.EntityIds.Count
        });

        return TypedResults.Ok<object>(result);
    }

    private static async Task<Ok<object>> GetEntitiesAsync(
        [FromServices] IEntityLocationService locationService,
        [FromQuery] string? domain,
        [FromQuery] string? agent,
        CancellationToken ct)
    {
        var entities = await locationService.GetEntitiesAsync(ct).ConfigureAwait(false);

        IEnumerable<HomeAssistantEntity> filtered = entities;

        if (!string.IsNullOrWhiteSpace(domain))
        {
            var domains = domain.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            filtered = filtered.Where(e => domains.Contains(e.Domain, StringComparer.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(agent))
        {
            filtered = filtered.Where(e =>
                e.IncludeForAgent is null ||
                e.IncludeForAgent.Contains(agent, StringComparer.OrdinalIgnoreCase));
        }

        var result = filtered.Select(e => new
        {
            e.EntityId,
            e.FriendlyName,
            e.Domain,
            e.Aliases,
            e.AreaId,
            embeddingGenerated = e.NameEmbedding is not null,
            AreaName = e.AreaId is not null ? locationService.GetAreaForEntity(e.EntityId)?.Name : null,
            FloorName = e.AreaId is not null ? locationService.GetFloorForArea(
                locationService.GetAreaForEntity(e.EntityId)?.AreaId ?? "")?.Name : null,
            IncludeForAgent = e.IncludeForAgent is null ? null : e.IncludeForAgent.ToList()
        });

        return TypedResults.Ok<object>(result);
    }

    private static async Task<Ok<object>> SearchAsync(
        [FromServices] IEntityLocationService locationService,
        [FromRoute] string term,
        [FromQuery] string? domain,
        [FromQuery] string? agent,
        CancellationToken ct)
    {
        var domainFilter = string.IsNullOrWhiteSpace(domain)
            ? null
            : (IReadOnlyList<string>)domain.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var entities = await locationService.SearchEntitiesAsync(term, domainFilter, ct: ct)
            .ConfigureAwait(false);

        // When impersonating an agent, filter to entities visible to that agent
        var filtered = string.IsNullOrWhiteSpace(agent)
            ? entities
            : entities.Where(e =>
                e.Entity.IncludeForAgent is null ||
                e.Entity.IncludeForAgent.Contains(agent, StringComparer.OrdinalIgnoreCase)).ToList();

        return TypedResults.Ok<object>(new
        {
            query = term,
            domainFilter = domain,
            agentFilter = agent,
            matchCount = filtered.Count,
            entities = filtered.Select(e => new
            {
                e.Entity.EntityId,
                e.Entity.FriendlyName,
                e.Entity.Domain,
                e.Entity.AreaId,
                embeddingGenerated = e.Entity.NameEmbedding is not null,
                AreaName = e.Entity.AreaId is not null ? locationService.GetAreaForEntity(e.Entity.EntityId)?.Name : null,
                e.HybridScore,
                e.EmbeddingSimilarity
            })
        });
    }

    private static async Task<Ok<object>> InvalidateAsync(
        [FromServices] IEntityLocationService locationService,
        CancellationToken ct)
    {
        await locationService.InvalidateAndReloadAsync(ct).ConfigureAwait(false);

        var floors = await locationService.GetFloorsAsync(ct).ConfigureAwait(false);
        var areas = await locationService.GetAreasAsync(ct).ConfigureAwait(false);
        var entities = await locationService.GetEntitiesAsync(ct).ConfigureAwait(false);

        return TypedResults.Ok<object>(new
        {
            message = "Cache invalidated and reloaded",
            floorCount = floors.Count,
            areaCount = areas.Count,
            entityCount = entities.Count,
            lastLoadedAt = locationService.LastLoadedAt?.ToString("O")
        });
    }

    private static async Task<Results<Ok<object>, NotFound<object>>> EvictEmbeddingAsync(
        [FromServices] IEntityLocationService locationService,
        [FromRoute] string itemType,
        [FromRoute] string itemId,
        CancellationToken ct)
    {
        var success = await locationService.EvictEmbeddingAsync(itemType, itemId, ct).ConfigureAwait(false);
        if (!success)
        {
            return TypedResults.NotFound<object>(new
            {
                message = "Item not found",
                itemType,
                itemId
            });
        }

        return TypedResults.Ok<object>(new
        {
            message = "Embedding evicted",
            itemType,
            itemId
        });
    }

    private static async Task<Results<Ok<object>, NotFound<object>, BadRequest<object>>> RegenerateEmbeddingAsync(
        [FromServices] IEntityLocationService locationService,
        [FromRoute] string itemType,
        [FromRoute] string itemId,
        CancellationToken ct)
    {
        var evicted = await locationService.EvictEmbeddingAsync(itemType, itemId, ct).ConfigureAwait(false);
        if (!evicted)
        {
            return TypedResults.NotFound<object>(new
            {
                message = "Item not found",
                itemType,
                itemId
            });
        }

        var regenerated = await locationService.RegenerateEmbeddingAsync(itemType, itemId, ct).ConfigureAwait(false);
        if (!regenerated)
        {
            return TypedResults.BadRequest<object>(new
            {
                message = "Embedding regeneration failed",
                itemType,
                itemId
            });
        }

        return TypedResults.Ok<object>(new
        {
            message = "Embedding regenerated",
            itemType,
            itemId
        });
    }
}
