using lucia.Agents.Abstractions;
using lucia.Agents.Models;

namespace lucia.Agents.Services;

/// <summary>
/// Orchestrates automatic entity-to-agent assignment using a composable rules chain.
/// </summary>
public sealed class AutoAssignEntityService(
    IEntityLocationService entityLocationService,
    IEnumerable<IOptimizableSkill> skills,
    IEnumerable<IEntityAssignmentRule> rules) : IAutoAssignEntityService
{
    private readonly IEntityLocationService _entityLocationService = entityLocationService;
    private readonly IReadOnlyList<IEntityAssignmentRule> _orderedRules = rules.OrderBy(r => r.Order).ToList();

    public async Task<AutoAssignPreview> PreviewAsync(AutoAssignStrategy strategy, CancellationToken ct = default)
    {
        var entities = await _entityLocationService.GetEntitiesAsync(ct).ConfigureAwait(false);
        var domainAgentMap = BuildDomainAgentMap();
        var entityAgentMap = new Dictionary<string, List<string>?>(StringComparer.OrdinalIgnoreCase);

        if (strategy is AutoAssignStrategy.None)
        {
            foreach (var entity in entities)
            {
                entityAgentMap[entity.EntityId] = [];
            }

            return BuildPreview(strategy, entities.Count, entityAgentMap);
        }

        // Smart strategy: evaluate rules chain for each entity
        foreach (var entity in entities)
        {
            var assigned = EvaluateRules(entity, domainAgentMap);
            entityAgentMap[entity.EntityId] = assigned;
        }

        return BuildPreview(strategy, entities.Count, entityAgentMap);
    }

    public async Task<AutoAssignResult> ApplyAsync(AutoAssignStrategy strategy, CancellationToken ct = default)
    {
        var preview = await PreviewAsync(strategy, ct).ConfigureAwait(false);

        await _entityLocationService.SetEntityAgentsAsync(
            new Dictionary<string, List<string>?>(preview.EntityAgentMap), ct).ConfigureAwait(false);

        return new AutoAssignResult
        {
            Strategy = strategy,
            TotalEntities = preview.TotalEntities,
            AssignedCount = preview.AssignedCount,
            ExcludedCount = preview.ExcludedCount,
        };
    }

    private List<string>? EvaluateRules(
        Models.HomeAssistant.HomeAssistantEntity entity,
        IReadOnlyDictionary<string, List<string>> domainAgentMap)
    {
        foreach (var rule in _orderedRules)
        {
            if (rule.TryEvaluate(entity, domainAgentMap, out var assignedAgents))
            {
                return assignedAgents;
            }
        }

        // No rule matched — default to excluded
        return [];
    }

    private Dictionary<string, List<string>> BuildDomainAgentMap()
    {
        var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var skill in skills)
        {
            if (string.IsNullOrEmpty(skill.AgentId))
                continue;

            foreach (var domain in skill.EntityDomains)
            {
                if (!map.TryGetValue(domain, out var agents))
                {
                    agents = [];
                    map[domain] = agents;
                }

                if (!agents.Contains(skill.AgentId, StringComparer.OrdinalIgnoreCase))
                {
                    agents.Add(skill.AgentId);
                }
            }
        }

        return map;
    }

    private static AutoAssignPreview BuildPreview(
        AutoAssignStrategy strategy,
        int totalEntities,
        Dictionary<string, List<string>?> entityAgentMap)
    {
        var agentGroups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var excluded = new List<string>();

        foreach (var (entityId, agents) in entityAgentMap)
        {
            if (agents is null or { Count: 0 })
            {
                excluded.Add(entityId);
                continue;
            }

            foreach (var agent in agents)
            {
                if (!agentGroups.TryGetValue(agent, out var entityList))
                {
                    entityList = [];
                    agentGroups[agent] = entityList;
                }
                entityList.Add(entityId);
            }
        }

        var groups = agentGroups
            .Select(kvp => new AutoAssignAgentGroup
            {
                AgentName = kvp.Key,
                Count = kvp.Value.Count,
                EntityIds = kvp.Value,
            })
            .OrderBy(g => g.AgentName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var assignedCount = entityAgentMap.Count(kvp => kvp.Value is { Count: > 0 });

        // Limit excluded sample for API response size
        const int maxExcludedSample = 50;
        var excludedSample = excluded.Count <= maxExcludedSample
            ? excluded
            : excluded.Take(maxExcludedSample).ToList();

        return new AutoAssignPreview
        {
            Strategy = strategy,
            TotalEntities = totalEntities,
            AssignedCount = assignedCount,
            ExcludedCount = excluded.Count,
            AgentGroups = groups,
            ExcludedSample = excludedSample,
            EntityAgentMap = entityAgentMap,
        };
    }
}
