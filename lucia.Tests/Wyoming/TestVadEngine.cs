using lucia.Wyoming.Vad;

namespace lucia.Tests.Wyoming;

internal sealed class TestVadEngine(IVadSession session) : IVadEngine
{
    public bool IsReady => true;

    public int CreateSessionCount { get; private set; }

    public IVadSession CreateSession()
    {
        CreateSessionCount++;
        return session;
    }

    public void Dispose()
    {
    }
}
