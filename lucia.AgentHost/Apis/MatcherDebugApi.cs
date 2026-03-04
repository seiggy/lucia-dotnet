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
        CancellationToken ct)
    {
        HybridMatchOptions? options = (threshold is not null || embeddingWeight is not null || dropoff is not null || disagreementPenalty is not null || embeddingResolutionMargin is not null)
            ? new HybridMatchOptions
            {
                Threshold = threshold ?? 0.55,
                EmbeddingWeight = embeddingWeight ?? 0.4,
                ScoreDropoffRatio = dropoff ?? 0.80,
                DisagreementPenalty = disagreementPenalty ?? 0.4,
                EmbeddingResolutionMargin = embeddingResolutionMargin ?? 0.30
            }
            : null;

        IReadOnlyList<string>? domainFilter = !string.IsNullOrWhiteSpace(domains)
            ? domains.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : null;

        var result = await locationService.SearchHierarchyAsync(term, options, domainFilter, ct).ConfigureAwait(false);

        return TypedResults.Ok<object>(new
        {
            query = term,
            options = new
            {
                threshold = options?.Threshold ?? 0.55,
                embeddingWeight = options?.EmbeddingWeight ?? 0.4,
                scoreDropoffRatio = options?.ScoreDropoffRatio ?? 0.80,
                disagreementPenalty = options?.DisagreementPenalty ?? 0.4,
                embeddingResolutionMargin = options?.EmbeddingResolutionMargin ?? 0.30,
                domainFilter = domainFilter ?? []
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
            entities = result.EntityMatches.Select(m => new
            {
                m.Entity.EntityId,
                m.Entity.FriendlyName,
                m.Entity.Domain,
                m.Entity.Aliases,
                m.Entity.AreaId,
                areaName = locationService.GetAreaForEntity(m.Entity.EntityId)?.Name,
                m.HybridScore,
                m.EmbeddingSimilarity
            }),
            resolvedEntities = result.ResolvedEntities.Select(e => new
            {
                e.EntityId,
                e.FriendlyName,
                e.Domain,
                e.AreaId,
                areaName = locationService.GetAreaForEntity(e.EntityId)?.Name
            }),
            summary = new
            {
                floorMatchCount = result.FloorMatches.Count,
                areaMatchCount = result.AreaMatches.Count,
                entityMatchCount = result.EntityMatches.Count,
                resolvedEntityCount = result.ResolvedEntities.Count
            }
        });
    }
}
