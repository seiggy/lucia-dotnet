namespace lucia.Wyoming.Wyoming;

public sealed record SessionConnectedEvent : SessionEvent
{
    public required string RemoteEndPoint { get; init; }
}
