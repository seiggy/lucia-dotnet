namespace lucia.AgentHost.Appliance;

public sealed record ApplianceOsStatus(
    string Name,
    string VersionId,
    string ImageVersion,
    string JetsonLinuxVersion)
{
    public bool? RootfsAbEnabled { get; init; }
    public string? UpdateBlockReason { get; init; }
}
