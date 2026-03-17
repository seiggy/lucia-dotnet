namespace lucia.Wyoming.Wyoming;

public sealed record DetectionEvent : WyomingEvent
{
    public override string Type => "detection";

    public string Name { get; init; } = string.Empty;

    public long? Timestamp { get; init; }
}
