namespace lucia.Wyoming.Audio;

public sealed class SpeechEnhancementOptions
{
    public const string SectionName = "Wyoming:Models:SpeechEnhancement";

    public bool Enabled { get; set; } = true;
    public string ActiveModel { get; set; } = "gtcrn_simple";
    public string ModelBasePath { get; set; } = "./models/speech-enhancement";
    public bool AutoDownloadDefault { get; set; } = true;
}
