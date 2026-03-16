using lucia.Wyoming.Stt;

namespace lucia.Tests.Wyoming;

internal sealed class TestSttSession(SttResult finalResult) : ISttSession
{
    public int AcceptAudioChunkCount { get; private set; }

    public bool IsEndOfUtterance => false;

    public void AcceptAudioChunk(ReadOnlySpan<float> samples, int sampleRate)
    {
        AcceptAudioChunkCount++;
    }

    public SttResult GetPartialResult()
    {
        return new SttResult();
    }

    public Task<SttResult> GetFinalResultAsync()
    {
        return Task.FromResult(finalResult);
    }

    public void Dispose()
    {
    }
}
