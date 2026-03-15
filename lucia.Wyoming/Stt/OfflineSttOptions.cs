namespace lucia.Wyoming.Stt;

/// <summary>
/// Configuration options for the Sherpa offline (non-streaming) STT engine.
/// Used for NeMo Parakeet, Whisper, SenseVoice, and other offline models.
/// </summary>
public sealed class OfflineSttOptions
{
    public const string SectionName = "Wyoming:Models:OfflineStt";

    /// <summary>Path to the offline STT model directory.</summary>
    public string ModelPath { get; set; } = string.Empty;

    /// <summary>Number of threads for inference.</summary>
    public int NumThreads { get; set; } = 4;

    /// <summary>Expected audio sample rate.</summary>
    public int SampleRate { get; set; } = 16000;

    /// <summary>ONNX Runtime execution provider (cpu, cuda).</summary>
    public string Provider { get; set; } = "cpu";

    /// <summary>Whether the offline STT engine is enabled.</summary>
    public bool Enabled { get; set; } = true;
}
