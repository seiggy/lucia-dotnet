namespace lucia.ApplianceManager;

public sealed record UpdateOperationStatus(
    string Action,
    string Channel,
    string Status,
    string? Tag,
    string? Message,
    bool LuciaRollbackAvailable = false,
    bool OsRollbackAvailable = false,
    string? OperationId = null,
    string? Phase = null,
    long? CompletedBytes = null,
    long? TotalBytes = null);
