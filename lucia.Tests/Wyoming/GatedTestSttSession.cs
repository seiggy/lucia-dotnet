using lucia.Wyoming.Stt;

namespace lucia.Tests.Wyoming;

internal sealed class GatedTestSttSession : ISttSession
{
    private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _disposeCount;

    public TaskCompletionSource FinalizationStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int DisposeCount => Volatile.Read(ref _disposeCount);

    public bool IsEndOfUtterance => false;

    public void AcceptAudioChunk(ReadOnlySpan<float> samples, int sampleRate)
    {
    }

    public SttResult GetPartialResult() => new();

    public async Task<SttResult> GetFinalResultAsync()
    {
        FinalizationStarted.TrySetResult();
        await _gate.Task;
        return new SttResult { Text = "done", Confidence = 1 };
    }

    public void Unblock() => _gate.TrySetResult();

    public void Dispose() => Interlocked.Increment(ref _disposeCount);
}
