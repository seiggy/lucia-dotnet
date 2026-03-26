using lucia.Agents.Abstractions;
using lucia.Agents.Models.HomeAssistant;

namespace lucia.Agents.Services.EntityAssignment;

/// <summary>
/// Maps entities to agents based on their Home Assistant domain using the
/// domain-to-agent mapping built from IOptimizableSkill registrations.
/// Entities in unmapped domains are excluded from all agents.
/// </summary>
public sealed class DomainMappingRule : IEntityAssignmentRule
{
    public int Order => 400;

    public bool TryEvaluate(
        HomeAssistantEntity entity,
        IReadOnlyDictionary<string, List<string>> domainAgentMap,
        out List<string>? assignedAgents)
    {
        if (domainAgentMap.TryGetValue(entity.Domain, out var agents) && agents.Count > 0)
        {
            assignedAgents = new List<string>(agents);
            return true;
        }

        // Domain not handled by any agent → exclude
        assignedAgents = [];
        return true;
    }
}
