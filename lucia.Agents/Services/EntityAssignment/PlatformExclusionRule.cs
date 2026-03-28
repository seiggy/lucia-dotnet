using lucia.Agents.Abstractions;
using lucia.Agents.Models.HomeAssistant;

namespace lucia.Agents.Services.EntityAssignment;

/// <summary>
/// Excludes entities from known infrastructure platforms (e.g., UniFi network equipment).
/// </summary>
public sealed class PlatformExclusionRule : IEntityAssignmentRule
{
    public int Order => 200;

    private static readonly HashSet<string> s_excludedPlatforms = new(StringComparer.OrdinalIgnoreCase)
    {
        "unifi",
    };

    public bool TryEvaluate(
        HomeAssistantEntity entity,
        IReadOnlyDictionary<string, List<string>> domainAgentMap,
        out List<string>? assignedAgents)
    {
        if (!string.IsNullOrEmpty(entity.Platform) && s_excludedPlatforms.Contains(entity.Platform))
        {
            assignedAgents = [];
            return true;
        }

        assignedAgents = null;
        return false;
    }
}
