namespace lucia.AgentHost.Models;

public sealed class AgentActivityStats
{
    public int RequestCount { get; init; }
    public double ErrorRate { get; init; }
}
