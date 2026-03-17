namespace lucia.Wyoming.Wyoming;

public sealed record VoiceStoppedEvent : WyomingEvent
{
    public override string Type => "voice-stopped";

    public long? Timestamp { get; init; }
}
