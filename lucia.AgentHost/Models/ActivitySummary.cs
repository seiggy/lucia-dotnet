namespace lucia.AgentHost.Models;

public sealed class ActivitySummary
{
    public required object Traces { get; init; }
    public required object Tasks { get; init; }
    public required object Cache { get; init; }
}
