namespace lucia.Wyoming.WakeWord;

/// <summary>
/// Wake word detection engine abstraction.
/// </summary>
public interface IWakeWordDetector
{
    /// <summary>Whether the detector is loaded and ready.</summary>
    bool IsReady { get; }

    /// <summary>Create a new wake word detection session.</summary>
    IWakeWordSession CreateSession();
}
