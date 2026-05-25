using System.Linq;
using lucia.Agents.Abstractions;
using lucia.Agents.Models.HomeAssistant;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace lucia.AgentHost.Apis;

/// <summary>
/// Minimal API endpoints for paginated entity queries used by the dashboard.
/// </summary>
public static class EntitiesApi
{
    private const int DefaultPageNumber = 1;
    private const int DefaultPageSize = 100;
    private const int MaxPageSize = 500;

    /// <summary>
    /// Maps the entity query API endpoints.
    /// </summary>
    public static IEndpointRouteBuilder MapEntitiesApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/entities", GetEntitiesAsync)
            .WithTags("Entities")
            .RequireAuthorization();

        return endpoints;
    }

    private static async Task<Ok<EntityQueryResponse>> GetEntitiesAsync(
        [FromServices] IEntityLocationService locationService,
        [FromQuery] string? nameFilter,
        [FromQuery] string? locationFilter,
        [FromQuery] string? domain,
        [FromQuery] string? agent,
        [FromQuery] int page = DefaultPageNumber,
        [FromQuery] int pageSize = DefaultPageSize,
        CancellationToken ct = default)
    {
        var normalizedPage = page < DefaultPageNumber ? DefaultPageNumber : page;
        var normalizedPageSize = pageSize switch
        {
            < 1 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => pageSize,
        };

        var domainFilter = ParseDomainFilter(domain);
        var filteredEntities = await GetFilteredEntitiesAsync(
            locationService,
            nameFilter,
            locationFilter,
            domainFilter,
            agent,
            ct).ConfigureAwait(false);

        var items = filteredEntities
            .Select(entity => CreateItem(locationService, entity))
            .ToList();

        var skip = (normalizedPage - 1) * normalizedPageSize;
        var pagedItems = items
            .Skip(skip)
            .Take(normalizedPageSize)
            .ToList();

        return TypedResults.Ok(new EntityQueryResponse
        {
            Items = pagedItems,
            TotalCount = items.Count,
            Page = normalizedPage,
            PageSize = normalizedPageSize,
        });
    }

    private static async Task<IReadOnlyList<HomeAssistantEntity>> GetFilteredEntitiesAsync(
        IEntityLocationService locationService,
        string? nameFilter,
        string? locationFilter,
        IReadOnlyList<string>? domainFilter,
        string? agent,
        CancellationToken ct)
    {
        var trimmedNameFilter = nameFilter?.Trim();
        var trimmedLocationFilter = locationFilter?.Trim();
        IEnumerable<HomeAssistantEntity> entities;

        if (!string.IsNullOrWhiteSpace(trimmedNameFilter))
        {
            var matches = await locationService.SearchEntitiesAsync(trimmedNameFilter, domainFilter, ct: ct)
                .ConfigureAwait(false);

            entities = matches
                .Select(match => match.Entity)
                .DistinctBy(entity => entity.EntityId, StringComparer.OrdinalIgnoreCase);

            if (!entities.Any())
            {
                var allEntities = await locationService.GetEntitiesAsync(ct).ConfigureAwait(false);
                if (domainFilter is { Count: > 0 })
                {
                    allEntities = allEntities
                        .Where(entity => domainFilter.Contains(entity.Domain, StringComparer.OrdinalIgnoreCase))
                        .ToList();
                }

                entities = allEntities.Where(entity =>
                    entity.EntityId.Contains(trimmedNameFilter, StringComparison.OrdinalIgnoreCase)
                    || entity.FriendlyName.Contains(trimmedNameFilter, StringComparison.OrdinalIgnoreCase)
                    || entity.Aliases.Any(alias => alias.Contains(trimmedNameFilter, StringComparison.OrdinalIgnoreCase)));
            }
        }
        else
        {
            entities = await locationService.GetEntitiesAsync(ct).ConfigureAwait(false);

            if (domainFilter is { Count: > 0 })
            {
                entities = entities.Where(entity => domainFilter.Contains(entity.Domain, StringComparer.OrdinalIgnoreCase));
            }
        }

        entities = entities
            .OrderBy(entity => entity.FriendlyName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entity => entity.EntityId, StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(trimmedLocationFilter))
        {
            entities = entities.Where(entity => MatchesLocationFilter(locationService, entity, trimmedLocationFilter));
        }

        if (!string.IsNullOrWhiteSpace(agent))
        {
            entities = entities.Where(entity =>
                entity.IncludeForAgent is null ||
                entity.IncludeForAgent.Contains(agent, StringComparer.OrdinalIgnoreCase));
        }

        return entities.ToList();
    }

    private static EntityQueryItem CreateItem(IEntityLocationService locationService, HomeAssistantEntity entity)
    {
        var area = locationService.GetAreaForEntity(entity.EntityId);
        var floor = area is null
            ? null
            : locationService.GetFloorForArea(area.AreaId);

        return new EntityQueryItem
        {
            EntityId = entity.EntityId,
            FriendlyName = entity.FriendlyName,
            Domain = entity.Domain,
            Aliases = entity.Aliases.ToList(),
            AreaId = entity.AreaId,
            AreaName = area?.Name,
            FloorName = floor?.Name,
            Platform = entity.Platform,
            EmbeddingGenerated = entity.NameEmbedding is not null,
            IncludeForAgent = entity.IncludeForAgent is null
                ? null
                : entity.IncludeForAgent
                    .OrderBy(agentName => agentName, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
        };
    }

    private static bool MatchesLocationFilter(
        IEntityLocationService locationService,
        HomeAssistantEntity entity,
        string locationFilter)
    {
        var area = locationService.GetAreaForEntity(entity.EntityId);
        var floor = area is null
            ? null
            : locationService.GetFloorForArea(area.AreaId);

        return ContainsIgnoreCase(entity.AreaId, locationFilter)
            || ContainsIgnoreCase(area?.Name, locationFilter)
            || ContainsIgnoreCase(floor?.Name, locationFilter)
            || ContainsAnyIgnoreCase(area?.Aliases, locationFilter)
            || ContainsAnyIgnoreCase(floor?.Aliases, locationFilter);
    }

    private static bool ContainsAnyIgnoreCase(IEnumerable<string>? values, string filter)
    {
        return values is not null && values.Any(value => ContainsIgnoreCase(value, filter));
    }

    private static bool ContainsIgnoreCase(string? value, string filter)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string>? ParseDomainFilter(string? domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            return null;
        }

        var filters = domain
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return filters.Count == 0 ? null : filters;
    }
}
