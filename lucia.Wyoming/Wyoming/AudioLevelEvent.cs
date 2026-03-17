namespace lucia.Wyoming.Wyoming;

public sealed record AudioLevelEvent : SessionEvent
{
    public required float RmsLevel { get; init; }
    public required int ActiveVoiceCount { get; init; }
}
