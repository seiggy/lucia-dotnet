namespace lucia.Wyoming.Stt;

/// <summary>
/// Abstraction for creating speech-to-text sessions.
/// </summary>
public interface ISttEngine : IDisposable
{
    /// <summary>
    /// Creates a new STT session for a single utterance.
    /// </summary>
    ISttSession CreateSession();
}
