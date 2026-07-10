using lucia.Wyoming.Stt;

namespace lucia.Tests.Wyoming;

/// <summary>
/// An <see cref="ISttEngine"/> that creates a new session per call using a provided factory.
/// Useful in concurrency tests where each session needs its own instance but shares state.
/// </summary>
internal sealed class FactoryTestSttEngine(Func<ISttSession> factory) : ISttEngine
{
    public bool IsReady => true;
    public ISttSession CreateSession() => factory();
    public void Dispose() { }
}
