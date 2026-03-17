namespace lucia.Wyoming.Stt;

/// <summary>
/// Represents the result of Granite speech-to-text transcription.
/// </summary>
public sealed record GraniteTranscript
{
    /// <summary>Decoded transcript text.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Confidence score (0.0 to 1.0).</summary>
    public float Confidence { get; init; }

    /// <summary>Inference wall-clock duration.</summary>
    public TimeSpan InferenceDuration { get; init; }
}
