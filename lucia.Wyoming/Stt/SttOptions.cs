namespace lucia.Wyoming.Stt;

public sealed class SttOptions
{
    public const string SectionName = "Wyoming:Models:Stt";

    public string ModelPath { get; set; } = string.Empty;

    public int NumThreads { get; set; } = 4;

    public int SampleRate { get; set; } = 16000;

    public string Provider { get; set; } = "cpu";
}
