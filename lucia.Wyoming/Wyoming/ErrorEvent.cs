namespace lucia.Wyoming.Wyoming;

public sealed record ErrorEvent : WyomingEvent
{
    public override string Type => "error";

    public string Text { get; init; } = string.Empty;

    public string? Code { get; init; }
}
