using lucia.Agents.Abstractions;
using lucia.Agents.Configuration;
using lucia.Agents.Configuration.UserConfiguration;
using lucia.Agents.Extensions;
using lucia.Agents.Providers;
using lucia.Agents.Registry;
using lucia.Agents.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace lucia.AgentHost.Apis;

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

        group.MapGet("/", ListDefinitionsAsync)
            .WithSummary("List all agent definitions")
            .WithDescription("Returns all user-defined and built-in agent definitions from MongoDB.");
        group.MapGet("/{id}", GetDefinitionAsync)
            .WithSummary("Get agent definition by ID");
        group.MapPost("/", CreateDefinitionAsync)
            .WithSummary("Create a new agent definition")
            .WithDescription("Creates a user-defined agent. Names that conflict with built-in agents are rejected.");
        group.MapPut("/{id}", ReplaceDefinitionAsync)
            .WithSummary("Replace an agent definition")
            .WithDescription("Fully replaces an existing definition using the route ID while preserving system-managed fields.");
        group.MapPatch("/{id}", PatchDefinitionAsync)
            .WithSummary("Patch an agent definition")
            .WithDescription("Merges provided fields into the existing definition. Null fields are ignored.");
        group.MapDelete("/{id}", DeleteDefinitionAsync)
            .WithSummary("Delete an agent definition")
            .WithDescription("Deletes the definition and unregisters the agent from the in-memory registry.");
        group.MapPost("/reload", ReloadAgentsAsync)
            .WithSummary("Reload dynamic agents")
            .WithDescription("Rebuilds all dynamic agents from their MongoDB definitions without restarting the host.");
        group.MapPost("/seed", SeedBuiltInAgentsAsync)
            .WithSummary("Seed built-in agent definitions")
            .WithDescription("Re-runs the built-in agent seed. Initializes and registers any newly added agents.");
        group.MapGet("/{id}/skill-config", GetSkillConfigAsync)
            .WithSummary("Get skill configuration for an agent")
            .WithDescription("Returns configurable skill sections with schemas and current values from MongoDB.");
        group.MapPut("/{id}/skill-config/{section}", UpdateSkillConfigAsync)
            .WithSummary("Update a skill configuration section")
            .WithDescription("Writes values to MongoDB config collection. Changes hot-reload via the config polling loop.");

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
            "lists-agent", "scene-agent", "security-agent"
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

    private static async Task<Results<Ok<AgentDefinition>, NotFound>> ReplaceDefinitionAsync(
        string id,
        [FromBody] AgentDefinition definition,
        [FromServices] IAgentDefinitionRepository repository)
    {
        var existing = await repository.GetAgentDefinitionAsync(id).ConfigureAwait(false);
        if (existing is null)
        {
            return TypedResults.NotFound();
        }

        var replacement = new AgentDefinition
        {
            Id = id,
            Name = definition.Name,
            DisplayName = definition.DisplayName,
            Description = definition.Description,
            Instructions = definition.Instructions,
            Tools = definition.Tools,
            ModelConnectionName = definition.ModelConnectionName,
            EmbeddingProviderName = definition.EmbeddingProviderName,
            Enabled = definition.Enabled,
            IsBuiltIn = existing.IsBuiltIn,
            IsRemote = existing.IsRemote,
            IsOrchestrator = existing.IsOrchestrator,
            CreatedAt = existing.CreatedAt,
            UpdatedAt = DateTime.UtcNow,
        };

        await repository.UpsertAgentDefinitionAsync(replacement).ConfigureAwait(false);
        return TypedResults.Ok(replacement);
    }

    private static async Task<Results<Ok<AgentDefinition>, NotFound>> PatchDefinitionAsync(
        string id,
        [FromBody] PatchAgentDefinitionRequest request,
        [FromServices] IAgentDefinitionRepository repository)
    {
        var existing = await repository.GetAgentDefinitionAsync(id).ConfigureAwait(false);
        if (existing is null)
        {
            return TypedResults.NotFound();
        }

        if (request.Name is not null)
        {
            existing.Name = request.Name;
        }

        if (request.DisplayName is not null)
        {
            existing.DisplayName = request.DisplayName;
        }

        if (request.Description is not null)
        {
            existing.Description = request.Description;
        }

        if (request.Instructions is not null)
        {
            existing.Instructions = request.Instructions;
        }

        if (request.ModelConnectionName is not null)
        {
            existing.ModelConnectionName = request.ModelConnectionName;
        }

        if (request.EmbeddingProviderName is not null)
        {
            existing.EmbeddingProviderName = request.EmbeddingProviderName;
        }

        if (request.Enabled.HasValue)
        {
            existing.Enabled = request.Enabled.Value;
        }

        if (request.Tools is not null)
        {
            existing.Tools = request.Tools;
        }

        existing.UpdatedAt = DateTime.UtcNow;

        // System-managed flags & timestamps — never overwrite from client data.
        // existing.IsBuiltIn, IsRemote, IsOrchestrator, CreatedAt remain unchanged.

        await repository.UpsertAgentDefinitionAsync(existing).ConfigureAwait(false);
        return TypedResults.Ok(existing);
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

    /// <summary>
    /// Returns skill configuration sections for an agent with schemas and current values.
    /// Schema is generated via reflection on the options types exposed by <see cref="ISkillConfigProvider"/>.
    /// </summary>
    private static async Task<Results<Ok<List<object>>, NotFound<string>>> GetSkillConfigAsync(
        [FromRoute] string id,
        [FromServices] IEnumerable<ILuciaAgent> agents,
        [FromServices] IConfigStoreWriter configStore,
        CancellationToken ct)
    {
        var provider = agents
            .OfType<ISkillConfigProvider>()
            .FirstOrDefault(a => string.Equals(((ILuciaAgent)a).GetAgentCard().Name, id, StringComparison.OrdinalIgnoreCase));

        if (provider is null)
            return TypedResults.NotFound($"Agent '{id}' has no configurable skills");

        var sections = new List<object>();

        foreach (var section in provider.GetSkillConfigSections())
        {
            var docs = await configStore.GetEntriesByKeyPrefixAsync($"{section.SectionName}:", ct).ConfigureAwait(false);

            var schema = BuildSchema(section.OptionsType);
            var currentValues = BuildCurrentValues(docs, section.SectionName, section.OptionsType);

            sections.Add(new
            {
                sectionName = section.SectionName,
                displayName = section.DisplayName,
                schema,
                values = currentValues
            });
        }

        return TypedResults.Ok(sections);
    }

    /// <summary>
    /// Updates a skill configuration section. Writes to the MongoDB config collection
    /// so changes hot-reload via the <see cref="MongoConfigurationProvider"/> polling loop.
    /// </summary>
    private static async Task<Results<Ok, NotFound<string>, BadRequest<string>>> UpdateSkillConfigAsync(
        [FromRoute] string id,
        [FromRoute] string section,
        [FromBody] Dictionary<string, object?> values,
        [FromServices] IEnumerable<ILuciaAgent> agents,
        [FromServices] IConfigStoreWriter configStore,
        CancellationToken ct)
    {
        var provider = agents
            .OfType<ISkillConfigProvider>()
            .FirstOrDefault(a => string.Equals(((ILuciaAgent)a).GetAgentCard().Name, id, StringComparison.OrdinalIgnoreCase));

        if (provider is null)
            return TypedResults.NotFound($"Agent '{id}' has no configurable skills");

        var configSection = provider.GetSkillConfigSections()
            .FirstOrDefault(s => string.Equals(s.SectionName, section, StringComparison.OrdinalIgnoreCase));

        if (configSection is null)
            return TypedResults.BadRequest($"Section '{section}' not found for agent '{id}'");

        foreach (var (key, value) in values)
        {
            if (value is System.Text.Json.JsonElement jsonElement && jsonElement.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                // Delete existing indexed keys then insert new ones
                await configStore.DeleteByKeyPrefixAsync($"{section}:{key}:", ct).ConfigureAwait(false);

                var items = jsonElement.EnumerateArray().Select(e => e.GetString()).Where(s => s is not null).ToList();
                if (items.Count > 0)
                {
                    var entries = items.Select((item, i) => new ConfigEntry
                    {
                        Key = $"{section}:{key}:{i}",
                        Value = item,
                        Section = section,
                        UpdatedAt = DateTime.UtcNow,
                        UpdatedBy = "admin-ui"
                    }).ToList();
                    await configStore.InsertManyAsync(entries, ct).ConfigureAwait(false);
                }
            }
            else
            {
                var fullKey = $"{section}:{key}";
                var stringValue = value?.ToString();
                await configStore.SetAsync(fullKey, stringValue, updatedBy: "admin-ui", cancellationToken: ct).ConfigureAwait(false);
            }
        }

        return TypedResults.Ok();
    }

    /// <summary>
    /// Generates a schema from an options type via reflection.
    /// </summary>
    private static List<object> BuildSchema(Type optionsType)
    {
        var schema = new List<object>();
        var defaults = Activator.CreateInstance(optionsType);

        foreach (var prop in optionsType.GetProperties())
        {
            if (string.Equals(prop.Name, "SectionName", StringComparison.Ordinal))
                continue;

            var propType = GetSchemaType(prop.PropertyType);
            var defaultValue = defaults is not null ? prop.GetValue(defaults) : null;

            schema.Add(new
            {
                name = prop.Name,
                type = propType,
                defaultValue
            });
        }

        return schema;
    }

    private static string GetSchemaType(Type type)
    {
        if (type == typeof(string)) return "string";
        if (type == typeof(int) || type == typeof(long)) return "integer";
        if (type == typeof(double) || type == typeof(float) || type == typeof(decimal)) return "number";
        if (type == typeof(bool)) return "boolean";
        if (type.IsGenericType && typeof(IEnumerable<string>).IsAssignableFrom(type)) return "string[]";
        return "string";
    }

    /// <summary>
    /// Reads current values from MongoDB config entries, falling back to defaults.
    /// Handles indexed array keys (e.g. EntityDomains:0, EntityDomains:1).
    /// </summary>
    private static Dictionary<string, object?> BuildCurrentValues(
        IReadOnlyList<ConfigEntry> docs, string sectionName, Type optionsType)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var defaults = Activator.CreateInstance(optionsType);
        var prefix = $"{sectionName}:";

        // Group docs by property name (handle array indexed keys)
        var byProperty = new Dictionary<string, List<(int? Index, string? Value)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var doc in docs)
        {
            var relativeKey = doc.Key[prefix.Length..];
            var parts = relativeKey.Split(':', 2);
            var propName = parts[0];

            if (!byProperty.TryGetValue(propName, out var list))
            {
                list = [];
                byProperty[propName] = list;
            }

            if (parts.Length > 1 && int.TryParse(parts[1], out var index))
                list.Add((index, doc.Value));
            else
                list.Add((null, doc.Value));
        }

        foreach (var prop in optionsType.GetProperties())
        {
            if (string.Equals(prop.Name, "SectionName", StringComparison.Ordinal))
                continue;

            if (byProperty.TryGetValue(prop.Name, out var entries))
            {
                if (entries.Any(e => e.Index is not null))
                {
                    // Array property — collect indexed values in order
                    result[prop.Name] = entries
                        .Where(e => e.Index is not null)
                        .OrderBy(e => e.Index)
                        .Select(e => e.Value)
                        .ToList();
                }
                else
                {
                    result[prop.Name] = entries.FirstOrDefault().Value;
                }
            }
            else
            {
                // Fall back to C# default
                result[prop.Name] = defaults is not null ? prop.GetValue(defaults) : null;
            }
        }

        return result;
    }
}
