namespace lucia.AgentHost.Extensions;

/// <summary>
/// Lightweight summary of an active task in Redis.
/// </summary>
public sealed class ActiveTaskSummary
{
    public required string Id { get; init; }
    public string? ContextId { get; init; }
    public required string Status { get; init; }
    public int MessageCount { get; init; }
    public string? UserInput { get; init; }
    public DateTime LastUpdated { get; init; }
}
