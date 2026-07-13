using lucia.Wyoming.Stt;

namespace lucia.Tests.Wyoming;

internal sealed class GatedTestSttSession : ISttSession
{
    private readonly TaskCompletionSource<SttResult> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _disposeGate =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly bool _blockDisposal;
    private int _disposeCount;

    public GatedTestSttSession(bool blockDisposal = false)
    {
        _blockDisposal = blockDisposal;
    }

    public TaskCompletionSource FinalizationStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource DisposalStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource DisposalCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int DisposeCount => Volatile.Read(ref _disposeCount);

    public bool IsEndOfUtterance => false;

    public void AcceptAudioChunk(ReadOnlySpan<float> samples, int sampleRate)
    {
    }

    public SttResult GetPartialResult() => new();

    public Task<SttResult> GetFinalResultAsync()
    {
        FinalizationStarted.TrySetResult();
        return _completion.Task;
    }

    public void Unblock() =>
        _completion.TrySetResult(new SttResult { Text = "done", Confidence = 1 });

    public void Fail(Exception exception) => _completion.TrySetException(exception);

    public void UnblockDisposal() => _disposeGate.TrySetResult();

    public void Dispose()
    {
        Interlocked.Increment(ref _disposeCount);
        DisposalStarted.TrySetResult();
        if (_blockDisposal)
        {
            _disposeGate.Task.GetAwaiter().GetResult();
        }

        DisposalCompleted.TrySetResult();
    }
}
