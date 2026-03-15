namespace lucia.Wyoming.Stt;

/// <summary>
/// Abstraction for creating speech-to-text sessions.
/// </summary>
public interface ISttEngine
{
    /// <summary>Whether the engine is loaded and ready.</summary>
    bool IsReady { get; }

    /// <summary>
    /// Creates a new STT session for a single utterance.
    /// </summary>
    ISttSession CreateSession();
}
