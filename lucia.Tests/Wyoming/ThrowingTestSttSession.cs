using lucia.Wyoming.Stt;

namespace lucia.Tests.Wyoming;

internal sealed class ThrowingTestSttSession : ISttSession
{
    public bool IsEndOfUtterance => false;

    public void AcceptAudioChunk(ReadOnlySpan<float> samples, int sampleRate)
    {
        throw new InvalidOperationException("Unexpected STT failure");
    }

    public SttResult GetPartialResult() => new();

    public Task<SttResult> GetFinalResultAsync() => Task.FromResult(new SttResult());

    public void Dispose()
    {
    }
}
