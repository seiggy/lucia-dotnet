using lucia.Agents.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace lucia.AgentHost.Extensions;

/// <summary>
/// Minimal API endpoints for viewing and managing the entity location cache.
/// </summary>
public static class EntityLocationCacheApi
{
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
        group.MapPost("/invalidate", InvalidateAsync);

        return endpoints;
    }

    private static async Task<Ok<object>> GetSummaryAsync(
        [FromServices] IEntityLocationService locationService,
        CancellationToken ct)
    {
        var floors = await locationService.GetFloorsAsync(ct).ConfigureAwait(false);
        var areas = await locationService.GetAreasAsync(ct).ConfigureAwait(false);
        var entities = await locationService.GetEntitiesAsync(ct).ConfigureAwait(false);

        return TypedResults.Ok<object>(new
        {
            floorCount = floors.Count,
            areaCount = areas.Count,
            entityCount = entities.Count,
            lastLoadedAt = locationService.LastLoadedAt?.ToString("O")
        });
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
            EntityCount = a.EntityIds.Count
        });

        return TypedResults.Ok<object>(result);
    }

    private static async Task<Ok<object>> GetEntitiesAsync(
        [FromServices] IEntityLocationService locationService,
        [FromQuery] string? domain,
        CancellationToken ct)
    {
        var entities = await locationService.GetEntitiesAsync(ct).ConfigureAwait(false);

        var filtered = string.IsNullOrWhiteSpace(domain)
            ? entities
            : entities.Where(e => e.Domain.Equals(domain, StringComparison.OrdinalIgnoreCase)).ToList();

        var result = filtered.Select(e => new
        {
            e.EntityId,
            e.FriendlyName,
            e.Domain,
            e.Aliases,
            e.AreaId,
            AreaName = e.AreaId is not null ? locationService.GetAreaForEntity(e.EntityId)?.Name : null,
            FloorName = e.AreaId is not null ? locationService.GetFloorForArea(
                locationService.GetAreaForEntity(e.EntityId)?.AreaId ?? "")?.Name : null
        });

        return TypedResults.Ok<object>(result);
    }

    private static async Task<Ok<object>> SearchAsync(
        [FromServices] IEntityLocationService locationService,
        [FromRoute] string term,
        [FromQuery] string? domain,
        CancellationToken ct)
    {
        var domainFilter = string.IsNullOrWhiteSpace(domain)
            ? null
            : new List<string> { domain };

        var entities = domainFilter is not null
            ? await locationService.FindEntitiesByLocationAsync(term, domainFilter, ct).ConfigureAwait(false)
            : await locationService.FindEntitiesByLocationAsync(term, ct).ConfigureAwait(false);

        return TypedResults.Ok<object>(new
        {
            query = term,
            domainFilter = domain,
            matchCount = entities.Count,
            entities = entities.Select(e => new
            {
                e.EntityId,
                e.FriendlyName,
                e.Domain,
                e.AreaId,
                AreaName = e.AreaId is not null ? locationService.GetAreaForEntity(e.EntityId)?.Name : null
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
}
