namespace lucia.Wyoming.WakeWord;

/// <summary>
/// A single wake word detection session, processing continuous audio.
/// </summary>
public interface IWakeWordSession : IDisposable
{
    /// <summary>Feed audio samples for wake word detection.</summary>
    void AcceptAudioChunk(ReadOnlySpan<float> samples, int sampleRate);

    /// <summary>Check if a wake word was detected. Returns null if none.</summary>
    WakeWordResult? CheckForDetection();

    /// <summary>Reset the detection state after a detection.</summary>
    void Reset();
}
