namespace lucia.Wyoming.Stt;

/// <summary>
/// Represents an incremental speech-to-text session.
/// </summary>
public interface ISttSession : IDisposable
{
    /// <summary>
    /// Accepts an audio chunk for decoding.
    /// </summary>
    void AcceptAudioChunk(ReadOnlySpan<float> samples, int sampleRate);

    /// <summary>
    /// Gets the current partial transcription result.
    /// </summary>
    SttResult GetPartialResult();

    /// <summary>
    /// Gets the final transcription result.
    /// </summary>
    SttResult GetFinalResult();

    /// <summary>
    /// Gets a value indicating whether the current utterance has ended.
    /// </summary>
    bool IsEndOfUtterance { get; }
}
