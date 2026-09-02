namespace lucia.AgentHost.Appliance;

public sealed record ApplianceUpdateRequest(
    string Tag,
    string? OperationId = null);
