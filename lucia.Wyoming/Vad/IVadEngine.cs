namespace lucia.Wyoming.Vad;

/// <summary>
/// Factory for per-connection voice activity detection sessions.
/// </summary>
public interface IVadEngine
{
    /// <summary>
    /// Gets a value indicating whether the engine is ready to create sessions.
    /// </summary>
    bool IsReady { get; }

    /// <summary>
    /// Creates a new voice activity detection session with isolated state.
    /// </summary>
    IVadSession CreateSession();
}
