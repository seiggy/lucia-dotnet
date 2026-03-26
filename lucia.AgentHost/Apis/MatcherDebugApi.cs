using lucia.Agents.Abstractions;
using lucia.Agents.Models;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace lucia.AgentHost.Apis;

/// <summary>
/// Debug API for troubleshooting the HybridEntityMatcher.
/// Exposes the hierarchical search that walks floors → areas → entities,
/// returning scored matches at every level plus resolved child entities.
/// </summary>
public static class MatcherDebugApi
{
    public static IEndpointRouteBuilder MapMatcherDebugApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/matcher-debug")
            .WithTags("MatcherDebug")
            .RequireAuthorization();

        group.MapGet("/search/{term}", SearchHierarchyAsync);

        return endpoints;
    }

    private static async Task<Ok<object>> SearchHierarchyAsync(
        [FromServices] IEntityLocationService locationService,
        [FromRoute] string term,
        [FromQuery] double? threshold,
        [FromQuery] double? embeddingWeight,
        [FromQuery] double? dropoff,
        [FromQuery] double? disagreementPenalty,
        [FromQuery] double? embeddingResolutionMargin,
        [FromQuery] string? domains,
        [FromQuery] string? agent,
        CancellationToken ct)
    {
        var defaults = new HybridMatchOptions();
        HybridMatchOptions? options = (threshold is not null || embeddingWeight is not null || dropoff is not null || disagreementPenalty is not null || embeddingResolutionMargin is not null)
            ? new HybridMatchOptions
            {
                Threshold = threshold ?? defaults.Threshold,
                EmbeddingWeight = embeddingWeight ?? defaults.EmbeddingWeight,
                ScoreDropoffRatio = dropoff ?? defaults.ScoreDropoffRatio,
                DisagreementPenalty = disagreementPenalty ?? defaults.DisagreementPenalty,
                EmbeddingResolutionMargin = embeddingResolutionMargin ?? defaults.EmbeddingResolutionMargin
            }
            : null;

        // Resolve effective options for response metadata
        var effective = options ?? defaults;

        IReadOnlyList<string>? domainFilter = !string.IsNullOrWhiteSpace(domains)
            ? domains.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : null;

        var hasAgent = !string.IsNullOrWhiteSpace(agent);

        var result = await locationService.SearchHierarchyAsync(term, options, domainFilter, ct).ConfigureAwait(false);

        bool IsVisibleToAgent(HashSet<string>? includeForAgent) =>
            !hasAgent ||
            includeForAgent is null ||
            includeForAgent.Contains(agent!, StringComparer.OrdinalIgnoreCase);

        var entityProjections = result.EntityMatches.Select(m => new
        {
            m.Entity.EntityId,
            m.Entity.FriendlyName,
            m.Entity.Domain,
            m.Entity.Aliases,
            m.Entity.AreaId,
            areaName = locationService.GetAreaForEntity(m.Entity.EntityId)?.Name,
            m.HybridScore,
            m.EmbeddingSimilarity,
            visibleToAgent = IsVisibleToAgent(m.Entity.IncludeForAgent)
        }).ToList();

        var resolvedProjections = result.ResolvedEntities.Select(e => new
        {
            e.EntityId,
            e.FriendlyName,
            e.Domain,
            e.AreaId,
            areaName = locationService.GetAreaForEntity(e.EntityId)?.Name,
            visibleToAgent = IsVisibleToAgent(e.IncludeForAgent)
        }).ToList();

        return TypedResults.Ok<object>(new
        {
            query = term,
            options = new
            {
                threshold = effective.Threshold,
                embeddingWeight = effective.EmbeddingWeight,
                scoreDropoffRatio = effective.ScoreDropoffRatio,
                disagreementPenalty = effective.DisagreementPenalty,
                embeddingResolutionMargin = effective.EmbeddingResolutionMargin,
                domainFilter = domainFilter ?? [],
                agentFilter = agent
            },
            resolution = new
            {
                strategy = result.ResolutionStrategy.ToString(),
                reason = result.ResolutionReason,
                bestEntityScore = result.BestEntityScore,
                bestAreaScore = result.BestAreaScore,
                bestFloorScore = result.BestFloorScore
            },
            floors = result.FloorMatches.Select(m => new
            {
                m.Entity.FloorId,
                m.Entity.Name,
                m.Entity.Aliases,
                m.Entity.Level,
                m.HybridScore,
                m.EmbeddingSimilarity
            }),
            areas = result.AreaMatches.Select(m => new
            {
                m.Entity.AreaId,
                m.Entity.Name,
                m.Entity.Aliases,
                m.Entity.FloorId,
                m.HybridScore,
                m.EmbeddingSimilarity
            }),
            entities = entityProjections,
            resolvedEntities = resolvedProjections,
            summary = new
            {
                floorMatchCount = result.FloorMatches.Count,
                areaMatchCount = result.AreaMatches.Count,
                entityMatchCount = result.EntityMatches.Count,
                resolvedEntityCount = result.ResolvedEntities.Count,
                visibleEntityMatchCount = hasAgent ? entityProjections.Count(e => e.visibleToAgent) : (int?)null,
                visibleResolvedEntityCount = hasAgent ? resolvedProjections.Count(e => e.visibleToAgent) : (int?)null
            }
        });
    }
}
