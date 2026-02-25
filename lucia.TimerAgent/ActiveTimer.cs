namespace lucia.TimerAgent;

/// <summary>
/// Represents a running timer that will announce on expiry.
/// </summary>
public sealed class ActiveTimer
{
    public required string Id { get; init; }
    public required string TaskId { get; init; }
    public required string Message { get; init; }
    public required string EntityId { get; init; }
    public required int DurationSeconds { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public required CancellationTokenSource Cts { get; init; }
}
