using lucia.Wyoming.WakeWord;

namespace lucia.Tests.Wyoming;

internal sealed class TestWakeWordSession(WakeWordResult? detection) : IWakeWordSession
{
    private WakeWordResult? _detection = detection;

    public int AcceptAudioChunkCount { get; private set; }

    public void AcceptAudioChunk(ReadOnlySpan<float> samples, int sampleRate)
    {
        AcceptAudioChunkCount++;
    }

    public WakeWordResult? CheckForDetection()
    {
        var result = _detection;
        _detection = null;
        return result;
    }

    public void Reset()
    {
        _detection = null;
    }

    public void Dispose()
    {
    }
}
