namespace lucia.Wyoming.Audio;

public sealed class SpeechEnhancementOptions
{
    public const string SectionName = "Wyoming:Models:SpeechEnhancement";

    public bool Enabled { get; set; } = true;
    public string ActiveModel { get; set; } = "gtcrn_simple";
    public string ModelBasePath { get; set; } = "./models/speech-enhancement";
    public bool AutoDownloadDefault { get; set; } = true;

    /// <summary>
    /// When enabled, the complete GTCRN-enhanced utterance clip is re-transcribed through
    /// a fresh STT session after VAD end-of-speech, and used for speaker verification.
    /// This avoids per-frame buffer discontinuities while benefiting from denoised audio.
    /// Default is <c>false</c> — existing raw-audio-to-STT path is used.
    /// </summary>
    public bool UseEnhancedClipForStt { get; set; }
}
