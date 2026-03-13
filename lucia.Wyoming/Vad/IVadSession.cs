namespace lucia.Wyoming.Vad;

/// <summary>
/// A per-connection VAD processing session with isolated state.
/// </summary>
public interface IVadSession : IDisposable
{
    void AcceptAudioChunk(ReadOnlySpan<float> samples);

    bool HasSpeechSegment { get; }

    VadSegment GetNextSegment();

    /// <summary>
    /// Flushes remaining audio to produce any final speech segment.
    /// </summary>
    void Flush();

    void Reset();
}
