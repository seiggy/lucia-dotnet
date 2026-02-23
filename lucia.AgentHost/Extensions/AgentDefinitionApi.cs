using lucia.Agents.Configuration;
using lucia.Agents.Mcp;
using lucia.Agents.Registry;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace lucia.AgentHost.Extensions;

/// <summary>
/// CRUD endpoints for user-defined agent definitions.
/// </summary>
public static class AgentDefinitionApi
{
    public static IEndpointRouteBuilder MapAgentDefinitionApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/agent-definitions")
            .WithTags("Agent Definitions")
            .RequireAuthorization();

        group.MapGet("/", ListDefinitionsAsync);
        group.MapGet("/{id}", GetDefinitionAsync);
        group.MapPost("/", CreateDefinitionAsync);
        group.MapPut("/{id}", UpdateDefinitionAsync);
        group.MapDelete("/{id}", DeleteDefinitionAsync);
        group.MapPost("/reload", ReloadAgentsAsync);

        return endpoints;
    }

    private static async Task<Ok<List<AgentDefinition>>> ListDefinitionsAsync(
        [FromServices] IAgentDefinitionRepository repository)
    {
        var definitions = await repository.GetAllAgentDefinitionsAsync().ConfigureAwait(false);
        return TypedResults.Ok(definitions);
    }

    private static async Task<Results<Ok<AgentDefinition>, NotFound>> GetDefinitionAsync(
        string id,
        [FromServices] IAgentDefinitionRepository repository)
    {
        var definition = await repository.GetAgentDefinitionAsync(id).ConfigureAwait(false);
        return definition is not null
            ? TypedResults.Ok(definition)
            : TypedResults.NotFound();
    }

    private static async Task<Results<Created<AgentDefinition>, Conflict<string>>> CreateDefinitionAsync(
        [FromBody] AgentDefinition definition,
        [FromServices] IAgentDefinitionRepository repository)
    {
        // Check for name conflicts with built-in agents
        var builtInNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "orchestrator", "light-agent", "climate-agent",
            "general-assistant", "music-agent", "timer-agent"
        };

        if (builtInNames.Contains(definition.Name))
        {
            return TypedResults.Conflict($"Agent name '{definition.Name}' conflicts with a built-in agent");
        }

        definition.CreatedAt = DateTime.UtcNow;
        definition.UpdatedAt = DateTime.UtcNow;
        await repository.UpsertAgentDefinitionAsync(definition).ConfigureAwait(false);
        return TypedResults.Created($"/api/agent-definitions/{definition.Id}", definition);
    }

    private static async Task<Results<Ok<AgentDefinition>, NotFound>> UpdateDefinitionAsync(
        string id,
        [FromBody] AgentDefinition definition,
        [FromServices] IAgentDefinitionRepository repository)
    {
        var existing = await repository.GetAgentDefinitionAsync(id).ConfigureAwait(false);
        if (existing is null) return TypedResults.NotFound();

        definition.Id = id;
        definition.CreatedAt = existing.CreatedAt;
        await repository.UpsertAgentDefinitionAsync(definition).ConfigureAwait(false);
        return TypedResults.Ok(definition);
    }

    private static async Task<Results<NoContent, NotFound>> DeleteDefinitionAsync(
        string id,
        [FromServices] IAgentDefinitionRepository repository,
        [FromServices] IDynamicAgentProvider dynamicAgentProvider,
        [FromServices] IAgentRegistry agentRegistry)
    {
        var existing = await repository.GetAgentDefinitionAsync(id).ConfigureAwait(false);
        if (existing is null) return TypedResults.NotFound();

        await repository.DeleteAgentDefinitionAsync(id).ConfigureAwait(false);

        // Unregister from in-memory provider and agent registry
        dynamicAgentProvider.Unregister(existing.Name);
        await agentRegistry.UnregisterAgentAsync($"/a2a/{existing.Name}").ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<Ok<string>> ReloadAgentsAsync(
        [FromServices] DynamicAgentLoader loader)
    {
        await loader.ReloadAsync().ConfigureAwait(false);
        return TypedResults.Ok("Dynamic agents reloaded");
    }
}
