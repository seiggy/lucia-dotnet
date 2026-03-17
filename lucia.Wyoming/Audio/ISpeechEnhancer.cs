namespace lucia.Wyoming.Audio;

/// <summary>
/// Speech enhancement engine factory. Creates per-stream sessions
/// with isolated state for streaming frame-by-frame enhancement.
/// </summary>
public interface ISpeechEnhancer
{
    bool IsReady { get; }

    /// <summary>
    /// Create a new enhancement session for a single audio stream.
    /// Each session maintains its own STFT buffers and GTCRN cache state.
    /// </summary>
    ISpeechEnhancerSession CreateSession();
}
