namespace lucia.Wyoming.Wyoming;

public sealed record AudioChunkEvent : WyomingEvent
{
    public override string Type => "audio-chunk";

    public int Rate { get; init; }

    public int Width { get; init; }

    public int Channels { get; init; }
}
