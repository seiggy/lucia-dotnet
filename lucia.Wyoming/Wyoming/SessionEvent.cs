namespace lucia.Wyoming.Wyoming;

/// <summary>
/// Base record for Wyoming session events published to the monitoring SSE stream.
/// </summary>
public abstract record SessionEvent
{
    public required string SessionId { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
