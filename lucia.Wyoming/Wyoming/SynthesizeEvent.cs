namespace lucia.Wyoming.Wyoming;

public sealed record SynthesizeEvent : WyomingEvent
{
    public override string Type => "synthesize";

    public string Text { get; init; } = string.Empty;

    public string? Voice { get; init; }

    public string? Language { get; init; }
}
