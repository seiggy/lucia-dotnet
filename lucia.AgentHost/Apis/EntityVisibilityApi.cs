using lucia.Agents.Abstractions;
using lucia.Agents.Configuration.UserConfiguration;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace lucia.AgentHost.Apis;

/// <summary>
/// API endpoints for managing per-entity agent visibility and the exposed-entity filter.
/// </summary>
public static class EntityVisibilityApi
{
    public static IEndpointRouteBuilder MapEntityVisibilityApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/entity-location/visibility")
            .WithTags("EntityVisibility")
            .RequireAuthorization();

        group.MapGet("/", GetVisibilityAsync);
        group.MapPut("/settings", UpdateSettingsAsync);
        group.MapPut("/agents", UpdateEntityAgentsAsync);
        group.MapDelete("/agents", ClearAllAgentFiltersAsync);
        group.MapGet("/available-agents", GetAvailableAgentsAsync);

        return endpoints;
    }

    /// <summary>
    /// Returns the full visibility config: exposed flag + per-entity agent mappings.
    /// </summary>
    private static async Task<Ok<object>> GetVisibilityAsync(
        [FromServices] IEntityLocationService locationService,
        CancellationToken ct)
    {
        var config = await locationService.GetVisibilityConfigAsync(ct).ConfigureAwait(false);

        return TypedResults.Ok<object>(new
        {
            useExposedEntitiesOnly = config.UseExposedEntitiesOnly,
            entityAgentMap = config.EntityAgentMap
        });
    }

    /// <summary>
    /// Update the exposed-entities-only flag.
    /// </summary>
    private static async Task<Ok<object>> UpdateSettingsAsync(
        [FromServices] IEntityLocationService locationService,
        [FromBody] VisibilitySettingsRequest request,
        CancellationToken ct)
    {
        await locationService.SetUseExposedOnlyAsync(request.UseExposedEntitiesOnly, ct).ConfigureAwait(false);

        return TypedResults.Ok<object>(new
        {
            message = "Settings updated",
            useExposedEntitiesOnly = request.UseExposedEntitiesOnly
        });
    }

    /// <summary>
    /// Bulk-update per-entity agent assignments.
    /// Each entry maps an entity_id to a list of agent names.
    /// <c>null</c> value = reset to visible-to-all.
    /// Empty list = exclude from all agents.
    /// </summary>
    private static async Task<Ok<object>> UpdateEntityAgentsAsync(
        [FromServices] IEntityLocationService locationService,
        [FromBody] Dictionary<string, List<string>?> updates,
        CancellationToken ct)
    {
        await locationService.SetEntityAgentsAsync(updates, ct).ConfigureAwait(false);

        return TypedResults.Ok<object>(new
        {
            message = "Agent visibility updated",
            updatedCount = updates.Count
        });
    }

    /// <summary>
    /// Clear all per-entity agent filters (resets everything to visible-to-all).
    /// </summary>
    private static async Task<Ok<object>> ClearAllAgentFiltersAsync(
        [FromServices] IEntityLocationService locationService,
        CancellationToken ct)
    {
        await locationService.ClearAllAgentFiltersAsync(ct).ConfigureAwait(false);

        return TypedResults.Ok<object>(new
        {
            message = "All agent filters cleared"
        });
    }

    /// <summary>
    /// Returns the list of available agents with their entity domains for the UI.
    /// Aggregates domains from <see cref="IOptimizableSkill"/> registrations.
    /// </summary>
    private static async Task<Ok<List<AgentInfo>>> GetAvailableAgentsAsync(
        [FromServices] IAgentDefinitionRepository repository,
        [FromServices] IEnumerable<IOptimizableSkill> skills,
        CancellationToken ct)
    {
        var definitions = await repository.GetAllAgentDefinitionsAsync(ct).ConfigureAwait(false);

        // Build agent → domains lookup from registered skills
        var domainsByAgent = skills
            .Where(s => !string.IsNullOrEmpty(s.AgentId))
            .GroupBy(s => s.AgentId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.SelectMany(s => s.EntityDomains).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                StringComparer.OrdinalIgnoreCase);

        var agents = definitions
            .Where(d => d.Enabled && !d.IsOrchestrator)
            .Select(d => new AgentInfo
            {
                Name = d.Name,
                Domains = domainsByAgent.GetValueOrDefault(d.Name) ?? []
            })
            .Where(a => a.Domains.Count > 0)
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return TypedResults.Ok(agents);
    }
}

/// <summary>
/// Request body for updating visibility settings.
/// </summary>
public sealed class VisibilitySettingsRequest
{
    public bool UseExposedEntitiesOnly { get; set; }
}
