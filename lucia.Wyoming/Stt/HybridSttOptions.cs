namespace lucia.Wyoming.Stt;

/// <summary>
/// Configuration for the hybrid streaming STT engine that uses an offline model
/// with periodic re-transcription of a growing audio buffer.
/// </summary>
public sealed class HybridSttOptions
{
    public const string SectionName = "Wyoming:Models:HybridStt";

    /// <summary>Path to the offline model directory (Parakeet TDT recommended).</summary>
    public string ModelPath { get; set; } = string.Empty;

    /// <summary>How often to re-transcribe the buffer (milliseconds).</summary>
    public int RefreshIntervalMs { get; set; } = 400;

    /// <summary>
    /// Maximum audio context to keep in the buffer (seconds).
    /// For very long utterances, older audio beyond this window is dropped.
    /// Set to 0 for unlimited (keep full utterance).
    /// </summary>
    public double MaxContextSeconds { get; set; } = 30.0;

    /// <summary>Minimum audio required before first transcription attempt (milliseconds).</summary>
    public int MinAudioMs { get; set; } = 300;

    /// <summary>Number of inference threads.</summary>
    public int NumThreads { get; set; } = 4;

    /// <summary>Expected sample rate.</summary>
    public int SampleRate { get; set; } = 16000;

    /// <summary>ONNX provider (cpu, cuda).</summary>
    public string Provider { get; set; } = "cpu";

    /// <summary>Whether the hybrid engine is enabled.</summary>
    public bool Enabled { get; set; } = true;
}
