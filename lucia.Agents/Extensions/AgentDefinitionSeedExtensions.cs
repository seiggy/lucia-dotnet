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
    public static async Task SeedBuiltInAgentDefinitionsAsync(
        this IAgentDefinitionRepository repository,
        IEnumerable<ILuciaAgent> agents,
        ILogger logger,
        CancellationToken ct = default)
    {
        var existing = await repository.GetAllAgentDefinitionsAsync(ct).ConfigureAwait(false);
        var existingIds = new HashSet<string>(existing.Select(d => d.Id), StringComparer.OrdinalIgnoreCase);

        foreach (var agent in agents)
        {
            var card = agent.GetAgentCard();
            var agentId = card.Name;

            if (string.IsNullOrWhiteSpace(agentId))
                continue;

            if (existingIds.Contains(agentId))
                continue;

            var definition = new AgentDefinition
            {
                Id = agentId,
                Name = agentId,
                DisplayName = FormatDisplayName(agentId),
                Description = card.Description ?? $"Built-in {agentId} agent",
                Instructions = string.Empty,
                IsBuiltIn = true,
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
