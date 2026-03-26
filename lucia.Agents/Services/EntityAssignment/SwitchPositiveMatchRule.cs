using lucia.Agents.Abstractions;
using lucia.Agents.Models.HomeAssistant;

namespace lucia.Agents.Services.EntityAssignment;

/// <summary>
/// Handles switch domain entities using positive keyword matching.
/// Switches are ambiguous — only those containing known device keywords
/// (e.g., "light", "fan") are assigned to the corresponding agent.
/// Unmatched switches default to no agents.
/// </summary>
public sealed class SwitchPositiveMatchRule : IEntityAssignmentRule
{
    public int Order => 300;

    private static readonly (string Keyword, string Domain)[] s_keywordMappings =
    [
        ("light", "light"),
        ("fan", "fan"),
    ];

    public bool TryEvaluate(
        HomeAssistantEntity entity,
        IReadOnlyDictionary<string, List<string>> domainAgentMap,
        out List<string>? assignedAgents)
    {
        if (!entity.Domain.Equals("switch", StringComparison.OrdinalIgnoreCase))
        {
            assignedAgents = null;
            return false;
        }

        var entityId = entity.EntityId;
        var friendlyName = entity.FriendlyName;

        foreach (var (keyword, domain) in s_keywordMappings)
        {
            if (entityId.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                friendlyName.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                if (domainAgentMap.TryGetValue(domain, out var agents))
                {
                    assignedAgents = new List<string>(agents);
                    return true;
                }
            }
        }

        // Switch with no keyword match → exclude from all agents
        assignedAgents = [];
        return true;
    }
}
