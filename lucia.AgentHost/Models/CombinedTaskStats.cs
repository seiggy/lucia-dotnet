using lucia.Agents.Models;

namespace lucia.AgentHost.Models;

/// <summary>
/// Combined stats from active and archived task stores.
/// </summary>
public sealed class CombinedTaskStats
{
    public int ActiveCount { get; init; }
    public required TaskStats Archived { get; init; }
}
