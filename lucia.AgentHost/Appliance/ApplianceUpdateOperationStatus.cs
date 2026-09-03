namespace lucia.AgentHost.Appliance;

public sealed record ApplianceUpdateOperationStatus(
    string Action,
    string Channel,
    string Status,
    string? Tag,
    string? Message,
    bool LuciaRollbackAvailable = false,
    bool OsRollbackAvailable = false,
    string? OperationId = null);
