namespace lucia.Wyoming.Stt;

/// <summary>
/// Offline speech-to-text engine using IBM Granite 4.0 Speech ONNX models.
/// Processes complete utterances for high-accuracy transcription.
/// </summary>
public interface IGraniteEngine
{
    /// <summary>Whether the ONNX models are loaded and ready for inference.</summary>
    bool IsReady { get; }

    /// <summary>
    /// Transcribes a complete audio utterance.
    /// </summary>
    /// <param name="audio">Audio samples normalized to [-1, 1].</param>
    /// <param name="sampleRate">Sample rate of the input audio (typically 16000).</param>
    /// <param name="keywordBias">Optional keyword list with per-keyword bias weights for biased decoding.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<GraniteTranscript> TranscribeAsync(
        float[] audio,
        int sampleRate,
        IReadOnlyList<KeywordBias>? keywordBias = null,
        CancellationToken ct = default);
}
