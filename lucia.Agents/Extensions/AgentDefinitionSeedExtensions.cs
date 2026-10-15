using System.Text.RegularExpressions;
using lucia.Agents.Abstractions;
using lucia.Agents.Configuration;
using lucia.Agents.Configuration.UserConfiguration;
using lucia.Agents.Orchestration;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.Extensions;

/// <summary>
/// Extension methods for seeding <see cref="AgentDefinition"/> documents
/// for built-in agents that are registered via DI but not yet present in the store.
/// </summary>
public static class AgentDefinitionSeedExtensions
{
    /// <summary>
    /// Ensures every <paramref name="agents"/> entry has a corresponding
    /// <see cref="AgentDefinition"/> in the store. Existing definitions are
    /// never overwritten, preserving user customizations.
    /// </summary>
    /// <param name="isRemote">
    /// When <c>true</c>, marks seeded definitions as remotely hosted (A2AHost plugins).
    /// Remote agents are not instantiated by the main AgentHost's dynamic loader.
    /// </param>
    public static async Task SeedBuiltInAgentDefinitionsAsync(
        this IAgentDefinitionRepository repository,
        IEnumerable<ILuciaAgent> agents,
        ILogger logger,
        CancellationToken ct = default,
        bool isRemote = false)
    {
        var existing = await repository.GetAllAgentDefinitionsAsync(ct).ConfigureAwait(false);
        var existingById = existing.ToDictionary(d => d.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var agent in agents)
        {
            var card = agent.GetAgentCard();
            var agentId = card.Name;

            if (string.IsNullOrWhiteSpace(agentId))
                continue;

            var isOrchestrator = agent is Agents.OrchestratorAgent;

            if (existingById.TryGetValue(agentId, out var existingDef))
            {
                // Ensure flags and display name are up-to-date on pre-existing definitions (migration)
                var needsUpdate = false;
                if (!existingDef.IsBuiltIn) { existingDef.IsBuiltIn = true; needsUpdate = true; }
                if (existingDef.IsRemote != isRemote) { existingDef.IsRemote = isRemote; needsUpdate = true; }
                if (existingDef.IsOrchestrator != isOrchestrator) { existingDef.IsOrchestrator = isOrchestrator; needsUpdate = true; }
                var webSearchDisplayName = agentId.Equals("general-assistant", StringComparison.OrdinalIgnoreCase)
                    && card.Skills?.Any(s => string.Equals(s.Name, "Web Search", StringComparison.OrdinalIgnoreCase)) == true;
                if (webSearchDisplayName && existingDef.DisplayName != "General & Web Search")
                {
                    existingDef.DisplayName = "General & Web Search";
                    needsUpdate = true;
                }

                if (needsUpdate)
                {
                    existingDef.UpdatedAt = DateTime.UtcNow;
                    await repository.UpsertAgentDefinitionAsync(existingDef, ct).ConfigureAwait(false);
                    logger.LogInformation("Updated flags on existing AgentDefinition '{AgentId}'.", agentId);
                }

                continue;
            }

            var displayName = FormatDisplayName(agentId);
            if (agentId.Equals("general-assistant", StringComparison.OrdinalIgnoreCase)
                && card.Skills?.Any(s => string.Equals(s.Name, "Web Search", StringComparison.OrdinalIgnoreCase)) == true)
            {
                displayName = "General & Web Search";
            }

            var definition = new AgentDefinition
            {
                Id = agentId,
                Name = agentId,
                DisplayName = displayName,
                Description = card.Description ?? $"Built-in {agentId} agent",
                Instructions = string.Empty,
                IsBuiltIn = true,
                IsRemote = isRemote,
                IsOrchestrator = isOrchestrator,
                Enabled = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await repository.UpsertAgentDefinitionAsync(definition, ct).ConfigureAwait(false);
            logger.LogInformation("Seeded AgentDefinition for built-in agent '{AgentId}'.", agentId);
        }
    }

    private static readonly Regex KebabCaseIdPattern = new("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.Compiled);

    /// <summary>
    /// One-time startup migration that makes the definition's Id the single routing identity.
    /// Legacy definitions may have drifted: older versions did not pin Name to Id, and ids
    /// were never validated, so a definition's kebab-case Id may differ from its Name or not
    /// be kebab-case at all. This method normalizes both while leaving DisplayName (the
    /// user-facing label) untouched:
    /// <list type="bullet">
    /// <item>Non-kebab-case ids are renamed to their slugified form; a rename whose target id
    /// already exists is skipped (it would silently replace another agent's definition).</item>
    /// <item>A Name that differs from the Id is pinned back to the Id.</item>
    /// </list>
    /// </summary>
    public static async Task MigrateRoutingIdentityAsync(
        this IAgentDefinitionRepository repository,
        ILogger logger,
        CancellationToken ct = default)
    {
        var definitions = await repository.GetAllAgentDefinitionsAsync(ct).ConfigureAwait(false);

        // Ids already in use (including ones created by an earlier rename in this pass),
        // so two legacy ids that slugify to the same target cannot clobber each other.
        var takenIds = new HashSet<string>(definitions.Select(d => d.Id), StringComparer.OrdinalIgnoreCase);

        foreach (var definition in definitions)
        {
            ct.ThrowIfCancellationRequested();

            if (!KebabCaseIdPattern.IsMatch(definition.Id))
            {
                var slug = RouterExecutor.ToAgentSlug(definition.Id);
                if (string.IsNullOrEmpty(slug))
                {
                    logger.LogWarning(
                        "Cannot migrate AgentDefinition '{OldId}': its id has no kebab-case representation.",
                        definition.Id);
                    continue;
                }

                if (!takenIds.Add(slug))
                {
                    logger.LogWarning(
                        "Cannot rename AgentDefinition '{OldId}' to '{NewId}': target id is already in use. " +
                        "Rename one of them manually to resolve the conflict.",
                        definition.Id, slug);
                    continue;
                }

                var renamed = new AgentDefinition
                {
                    Id = slug,
                    Name = slug,
                    DisplayName = definition.DisplayName,
                    Description = definition.Description,
                    Instructions = definition.Instructions,
                    Tools = definition.Tools,
                    ModelConnectionName = definition.ModelConnectionName,
                    EmbeddingProviderName = definition.EmbeddingProviderName,
                    Enabled = definition.Enabled,
                    IsBuiltIn = definition.IsBuiltIn,
                    IsRemote = definition.IsRemote,
                    IsOrchestrator = definition.IsOrchestrator,
                    CreatedAt = definition.CreatedAt,
                    UpdatedAt = DateTime.UtcNow
                };

                await repository.UpsertAgentDefinitionAsync(renamed, ct).ConfigureAwait(false);
                await repository.DeleteAgentDefinitionAsync(definition.Id, ct).ConfigureAwait(false);
                logger.LogInformation(
                    "Migrated AgentDefinition id '{OldId}' to kebab-case id '{NewId}'.",
                    definition.Id, slug);
            }
            else if (definition.Name != definition.Id)
            {
                var oldName = definition.Name;
                definition.Name = definition.Id;
                definition.UpdatedAt = DateTime.UtcNow;
                await repository.UpsertAgentDefinitionAsync(definition, ct).ConfigureAwait(false);
                logger.LogInformation(
                    "Pinned AgentDefinition '{Id}' Name to its Id (was '{OldName}').",
                    definition.Id, oldName);
            }
        }
    }

    private static string FormatDisplayName(string agentId)
    {
        // "light-agent" → "Light Agent", "general-assistant" → "General Assistant"
        return string.Join(' ', agentId.Split('-').Select(w =>
            string.IsNullOrEmpty(w) ? w : char.ToUpperInvariant(w[0]) + w[1..]));
    }
}
