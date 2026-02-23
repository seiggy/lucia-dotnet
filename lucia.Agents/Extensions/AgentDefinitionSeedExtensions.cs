using lucia.Agents.Abstractions;
using lucia.Agents.Configuration;
using lucia.Agents.Mcp;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.Extensions;

/// <summary>
/// Extension methods for seeding <see cref="AgentDefinition"/> documents
/// for built-in agents that are registered via DI but not yet present in MongoDB.
/// </summary>
public static class AgentDefinitionSeedExtensions
{
    /// <summary>
    /// Ensures every <paramref name="agents"/> entry has a corresponding
    /// <see cref="AgentDefinition"/> in MongoDB. Existing definitions are
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
                // Ensure flags are up-to-date on pre-existing definitions (migration)
                var needsUpdate = false;
                if (!existingDef.IsBuiltIn) { existingDef.IsBuiltIn = true; needsUpdate = true; }
                if (existingDef.IsRemote != isRemote) { existingDef.IsRemote = isRemote; needsUpdate = true; }
                if (existingDef.IsOrchestrator != isOrchestrator) { existingDef.IsOrchestrator = isOrchestrator; needsUpdate = true; }

                if (needsUpdate)
                {
                    existingDef.UpdatedAt = DateTime.UtcNow;
                    await repository.UpsertAgentDefinitionAsync(existingDef, ct).ConfigureAwait(false);
                    logger.LogInformation("Updated flags on existing AgentDefinition '{AgentId}'.", agentId);
                }

                continue;
            }

            var definition = new AgentDefinition
            {
                Id = agentId,
                Name = agentId,
                DisplayName = FormatDisplayName(agentId),
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

    private static string FormatDisplayName(string agentId)
    {
        // "light-agent" → "Light Agent", "general-assistant" → "General Assistant"
        return string.Join(' ', agentId.Split('-').Select(w =>
            string.IsNullOrEmpty(w) ? w : char.ToUpperInvariant(w[0]) + w[1..]));
    }
}
