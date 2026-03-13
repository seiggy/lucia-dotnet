namespace lucia.Wyoming.Wyoming;

/// <summary>
/// Base record for all Wyoming protocol events.
/// </summary>
public abstract record WyomingEvent
{
    public abstract string Type { get; }

    public byte[]? Payload { get; init; }
}
