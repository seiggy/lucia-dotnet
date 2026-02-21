using lucia.Agents.Services;

namespace lucia.AgentHost.Extensions;

/// <summary>
/// Combined stats from active and archived task stores.
/// </summary>
public sealed class CombinedTaskStats
{
    public int ActiveCount { get; init; }
    public required TaskStats Archived { get; init; }
}
