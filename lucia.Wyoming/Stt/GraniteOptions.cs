namespace lucia.Wyoming.Stt;

/// <summary>
/// Configuration options for the Granite offline speech-to-text engine.
/// </summary>
public sealed class GraniteOptions
{
    public const string SectionName = "Wyoming:Models:GraniteStt";

    /// <summary>Path to the Granite model directory containing ONNX models and tokenizer.</summary>
    public string ModelPath { get; set; } = string.Empty;

    /// <summary>Number of threads for ONNX Runtime inference.</summary>
    public int NumThreads { get; set; } = 4;

    /// <summary>Expected audio sample rate.</summary>
    public int SampleRate { get; set; } = 16000;

    /// <summary>ONNX Runtime execution provider (cpu, cuda).</summary>
    public string Provider { get; set; } = "cpu";

    /// <summary>Maximum number of tokens to generate during decoding.</summary>
    public int MaxTokens { get; set; } = 448;

    /// <summary>Whether the Granite engine is enabled.</summary>
    public bool Enabled { get; set; } = true;
}
