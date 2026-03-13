namespace lucia.Wyoming.Audio;

/// <summary>
/// Represents audio stream parameters.
/// </summary>
public sealed record AudioFormat
{
    public required int SampleRate { get; init; }
    public required int BitsPerSample { get; init; }
    public required int Channels { get; init; }

    public int BytesPerSample => BitsPerSample / 8;
    public int BytesPerFrame => Channels * BytesPerSample;
    public int BytesPerSecond => SampleRate * BytesPerFrame;

    /// <summary>Standard 16kHz 16-bit mono PCM (Wyoming/sherpa-onnx default).</summary>
    public static AudioFormat Default => new()
    {
        SampleRate = 16000,
        BitsPerSample = 16,
        Channels = 1,
    };
}
