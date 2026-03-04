namespace lucia.Agents.Orchestration.Models;

/// <summary>
/// Configuration for agent invokers.
/// </summary>
public sealed class AgentInvokerOptions
{
    /// <summary>
    /// Maximum execution duration before timing out.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}
