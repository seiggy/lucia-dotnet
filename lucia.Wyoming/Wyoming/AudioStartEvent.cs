namespace lucia.Wyoming.Wyoming;

public sealed record AudioStartEvent : WyomingEvent
{
    public override string Type => "audio-start";

    public int Rate { get; init; }

    public int Width { get; init; }

    public int Channels { get; init; }
}
