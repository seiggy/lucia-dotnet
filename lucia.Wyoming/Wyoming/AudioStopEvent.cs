namespace lucia.Wyoming.Wyoming;

public sealed record AudioStopEvent : WyomingEvent
{
    public override string Type => "audio-stop";
}
