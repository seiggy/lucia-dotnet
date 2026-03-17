using lucia.Wyoming.Stt;

namespace lucia.Tests.Wyoming;

internal sealed class TestSttEngine(ISttSession session) : ISttEngine
{
    public bool IsReady => true;

    public int CreateSessionCount { get; private set; }

    public ISttSession CreateSession()
    {
        CreateSessionCount++;
        return session;
    }

    public void Dispose()
    {
    }
}
