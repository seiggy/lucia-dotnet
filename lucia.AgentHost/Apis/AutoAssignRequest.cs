using lucia.Agents.Models;

namespace lucia.AgentHost.Apis;

/// <summary>
/// Request body for auto-assign preview and apply endpoints.
/// </summary>
public sealed class AutoAssignRequest
{
    public AutoAssignStrategy Strategy { get; set; }
}
