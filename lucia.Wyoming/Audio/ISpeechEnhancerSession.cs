namespace lucia.Wyoming.Audio;

/// <summary>
/// A per-stream speech enhancement session with persistent state.
/// Maintains STFT overlap buffers and GTCRN cache tensors across frames.
/// Created via <see cref="ISpeechEnhancer.CreateSession"/> for each Wyoming audio stream.
/// </summary>
public interface ISpeechEnhancerSession : IDisposable
{
    /// <summary>
    /// Process a chunk of noisy audio samples and return enhanced audio.
    /// Handles internal frame buffering — input chunks of any size are accepted.
    /// Output length may differ from input due to STFT hop alignment.
    /// </summary>
    float[] Process(float[] samples);
}
