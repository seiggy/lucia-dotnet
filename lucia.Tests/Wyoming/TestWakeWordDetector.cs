using lucia.Wyoming.WakeWord;

namespace lucia.Tests.Wyoming;

internal sealed class TestWakeWordDetector(IWakeWordSession session) : IWakeWordDetector
{
    public bool IsReady => true;

    public int CreateSessionCount { get; private set; }

    public IWakeWordSession CreateSession()
    {
        CreateSessionCount++;
        return session;
    }

    public void Dispose()
    {
    }
}
