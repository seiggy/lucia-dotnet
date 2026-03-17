namespace lucia.Wyoming.Wyoming;

public sealed record InfoEvent : WyomingEvent
{
    public override string Type => "info";

    public AsrInfo[]? Asr { get; init; }

    public TtsInfo[]? Tts { get; init; }

    public WakeInfo[]? Wake { get; init; }

    public string? Version { get; init; }
}
