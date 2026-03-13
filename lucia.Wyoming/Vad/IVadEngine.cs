namespace lucia.Wyoming.Vad;

/// <summary>
/// Abstraction for voice activity detection.
/// </summary>
public interface IVadEngine : IDisposable
{
    /// <summary>
    /// Accepts an audio chunk for VAD analysis.
    /// </summary>
    void AcceptAudioChunk(ReadOnlySpan<float> samples);

    /// <summary>
    /// Gets a value indicating whether a speech segment is ready.
    /// </summary>
    bool HasSpeechSegment { get; }

    /// <summary>
    /// Gets the next available speech segment.
    /// </summary>
    VadSegment GetNextSegment();

    /// <summary>
    /// Resets the VAD state.
    /// </summary>
    void Reset();
}
