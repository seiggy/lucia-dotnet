namespace lucia.Wyoming.Wyoming;

public sealed record DetectEvent : WyomingEvent
{
    public override string Type => "detect";

    public string[]? Names { get; init; }
}
