namespace lucia.AgentHost.Models;

public sealed record MergeProfilesRequest
{
    public required string SourceProfileId { get; init; }
    public required string TargetProfileId { get; init; }
}
