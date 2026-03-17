using lucia.Wyoming.Vad;

namespace lucia.Tests.Wyoming;

internal sealed class TestVadSession(VadSegment flushedSegment) : IVadSession
{
    private readonly Queue<VadSegment> _segments = new();

    public int AcceptAudioChunkCount { get; private set; }

    public int FlushCallCount { get; private set; }

    public bool HasSpeechSegment => _segments.Count > 0;

    public void AcceptAudioChunk(ReadOnlySpan<float> samples)
    {
        AcceptAudioChunkCount++;
    }

    public VadSegment GetNextSegment() => _segments.Dequeue();

    public void Flush()
    {
        FlushCallCount++;
        _segments.Enqueue(flushedSegment);
    }

    public void Reset()
    {
        _segments.Clear();
    }

    public void Dispose()
    {
    }
}
