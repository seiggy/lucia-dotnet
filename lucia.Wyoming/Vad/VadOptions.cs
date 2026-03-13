namespace lucia.Wyoming.Vad;

public sealed class VadOptions
{
    public const string SectionName = "Wyoming:Models:Vad";

    public string ModelPath { get; set; } = "";

    public float Threshold { get; set; } = 0.5f;

    public float MinSpeechDuration { get; set; } = 0.25f;

    public float MinSilenceDuration { get; set; } = 0.5f;

    public int SampleRate { get; set; } = 16000;
}
