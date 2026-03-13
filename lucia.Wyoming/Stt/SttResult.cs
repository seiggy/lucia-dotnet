namespace lucia.Wyoming.Stt;

/// <summary>
/// Represents a speech-to-text decoding result.
/// </summary>
public sealed record SttResult
{
    public string Text { get; init; } = string.Empty;
    public float Confidence { get; init; }
}
