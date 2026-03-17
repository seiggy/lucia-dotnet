namespace lucia.Wyoming.Wyoming;

public sealed record TranscribeEvent : WyomingEvent
{
    public override string Type => "transcribe";

    public string? Name { get; init; }

    public string? Language { get; init; }
}
