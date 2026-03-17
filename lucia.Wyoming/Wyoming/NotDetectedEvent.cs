namespace lucia.Wyoming.Wyoming;

public sealed record NotDetectedEvent : WyomingEvent
{
    public override string Type => "not-detected";
}
