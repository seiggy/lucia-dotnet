namespace lucia.Wyoming.WakeWord;

public sealed class WakeWordOptions
{
    public const string SectionName = "Wyoming:Models:WakeWord";

    public string ModelPath { get; set; } = "";
    public string[] DefaultKeywords { get; set; } = ["hey lucia"];
    public string KeywordsFile { get; set; } = "";
    public float Sensitivity { get; set; } = 0.5f;
    public int MaxConcurrentKeywords { get; set; } = 10;
    public bool AllowCustomWakeWords { get; set; } = true;
}
