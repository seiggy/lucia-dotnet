using lucia.Agents.Abstractions;
using lucia.Agents.Configuration;
using lucia.Agents.Extensions;
using lucia.Agents.Mcp;
using lucia.Agents.Providers;
using lucia.Agents.Registry;
using lucia.Agents.Services;
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
        group.MapPost("/seed", SeedBuiltInAgentsAsync);

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
            "general-assistant", "music-agent", "timer-agent",
            "lists-agent", "scene-agent"
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

    /// <summary>
    /// Re-runs the built-in agent definition seed and initializes/registers any newly added agents.
    /// Use this to add missing built-in agents (e.g. lists-agent) to an existing deployment without restarting.
    /// </summary>
    private static async Task<Ok<string>> SeedBuiltInAgentsAsync(
        [FromServices] IAgentDefinitionRepository repository,
        [FromServices] IEnumerable<ILuciaAgent> agents,
        [FromServices] IAgentRegistry agentRegistry,
        [FromServices] ILoggerFactory loggerFactory,
        CancellationToken cancellationToken = default)
    {
        var logger = loggerFactory.CreateLogger("AgentDefinitionApi");
        var existingBefore = (await repository.GetAllAgentDefinitionsAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(d => d.Id, StringComparer.OrdinalIgnoreCase);

        await repository.SeedBuiltInAgentDefinitionsAsync(agents, logger, cancellationToken).ConfigureAwait(false);

        var existingAfter = (await repository.GetAllAgentDefinitionsAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(d => d.Id, StringComparer.OrdinalIgnoreCase);

        var newlyAdded = new List<string>();
        foreach (var agent in agents)
        {
            var agentId = agent.GetAgentCard().Name;
            if (string.IsNullOrWhiteSpace(agentId)) continue;
            if (existingBefore.ContainsKey(agentId)) continue;
            if (!existingAfter.ContainsKey(agentId)) continue;
            newlyAdded.Add(agentId);
        }

        foreach (var agent in agents)
        {
            var agentId = agent.GetAgentCard().Name;
            if (!newlyAdded.Contains(agentId)) continue;

            try
            {
                await agent.InitializeAsync(cancellationToken).ConfigureAwait(false);
                var card = agent.GetAgentCard();
                await agentRegistry.RegisterAgentAsync(card, cancellationToken).ConfigureAwait(false);
                logger.LogInformation("Initialized and registered newly seeded agent '{AgentId}'", agentId);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not initialize/register agent '{AgentId}' after seed", agentId);
            }
        }

        return TypedResults.Ok("Built-in agent definitions seeded");
    }
}
