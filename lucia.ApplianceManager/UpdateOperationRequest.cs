namespace lucia.ApplianceManager;

public sealed record UpdateOperationRequest(
    string? Tag,
    string? OperationId = null);
