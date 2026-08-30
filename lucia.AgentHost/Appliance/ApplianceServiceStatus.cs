namespace lucia.AgentHost.Appliance;

public sealed record ApplianceServiceStatus(
    string Id,
    string ActiveState,
    string UnitFileState);
