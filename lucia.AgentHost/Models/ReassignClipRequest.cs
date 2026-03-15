namespace lucia.AgentHost.Models;

public sealed record ReassignClipRequest
{
    public required string TargetProfileId { get; init; }
}
