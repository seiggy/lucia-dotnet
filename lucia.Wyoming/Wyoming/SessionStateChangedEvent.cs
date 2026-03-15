namespace lucia.Wyoming.Wyoming;

public sealed record SessionStateChangedEvent : SessionEvent
{
    public required WyomingSessionState State { get; init; }
}
