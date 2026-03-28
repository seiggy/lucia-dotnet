namespace lucia.Agents.Models;

/// <summary>
/// Result of applying an auto-assign operation.
/// </summary>
public sealed class AutoAssignResult
{
    public required AutoAssignStrategy Strategy { get; init; }
    public required int TotalEntities { get; init; }
    public required int AssignedCount { get; init; }
    public required int ExcludedCount { get; init; }
}
