using lucia.Agents.Models.HomeAssistant;

namespace lucia.Agents.Abstractions;

/// <summary>
/// A single rule in the entity assignment rules chain. Rules are evaluated in order;
/// the first rule that returns true determines the entity's assignment.
/// </summary>
public interface IEntityAssignmentRule
{
    /// <summary>
    /// Rule evaluation order. Lower values run first.
    /// </summary>
    int Order { get; }

    /// <summary>
    /// Evaluate whether this rule has a verdict for the given entity.
    /// </summary>
    /// <param name="entity">The entity to evaluate.</param>
    /// <param name="domainAgentMap">Mapping of HA entity domains to agent names.</param>
    /// <param name="assignedAgents">
    /// If the rule matches: the list of agents to assign (empty list = exclude from all agents, null = skip/no opinion).
    /// </param>
    /// <returns>True if this rule has a verdict; false to pass to the next rule.</returns>
    bool TryEvaluate(
        HomeAssistantEntity entity,
        IReadOnlyDictionary<string, List<string>> domainAgentMap,
        out List<string>? assignedAgents);
}
