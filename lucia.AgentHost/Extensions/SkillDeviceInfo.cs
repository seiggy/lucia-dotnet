namespace lucia.AgentHost.Extensions;

/// <summary>API response describing a device in a skill's cache.</summary>
public sealed record SkillDeviceInfo
{
    public required string EntityId { get; init; }
    public required string FriendlyName { get; init; }
}
