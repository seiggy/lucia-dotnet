namespace lucia.Wyoming.Wyoming;

public sealed record VoiceStartedEvent : WyomingEvent
{
    public override string Type => "voice-started";

    public long? Timestamp { get; init; }
}
