using lucia.Wyoming.Stt;

namespace lucia.Tests.Wyoming;

/// <summary>
/// A test STT session whose <see cref="GetFinalResultAsync"/> blocks until <see cref="Unblock"/> is called.
/// Implements <see cref="IAsyncDisposable"/>; concurrent callers of <see cref="DisposeAsync"/> all await
/// the same gate task so no caller returns before inference completes — matching the invariant that
/// <see cref="WyomingSession.DisposeCurrentSttSession"/> waits for inference before releasing the STT slot.
/// </summary>
internal sealed class BlockingTestSttSession : ISttSession, IAsyncDisposable
{
    private readonly SttResult _result;
    private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    // Shared disposal task: all concurrent DisposeAsync callers await the SAME gate-backed task,
    // so the STT slot is not released until inference (Unblock) has actually finished.
    private readonly Lazy<Task> _disposeTask;

    public BlockingTestSttSession(SttResult result)
    {
        _result = result;
        _disposeTask = new Lazy<Task>(() => _gate.Task, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>Called when <see cref="GetFinalResultAsync"/> is invoked — lets tests synchronize on this point.</summary>
    public TaskCompletionSource InferenceStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Unblocks the pending <see cref="GetFinalResultAsync"/> call and all <see cref="DisposeAsync"/> waiters.</summary>
    public void Unblock() => _gate.TrySetResult();

    public bool IsEndOfUtterance => false;
    public void AcceptAudioChunk(ReadOnlySpan<float> samples, int sampleRate) { }
    public SttResult GetPartialResult() => new();

    public async Task<SttResult> GetFinalResultAsync()
    {
        InferenceStarted.TrySetResult();
        await _gate.Task;
        return _result;
    }

    /// <summary>
    /// Returns the shared gate task. All concurrent callers await the SAME completion,
    /// matching <see cref="HybridSttSession"/>'s <see cref="HybridSttSession.DisposeAsync"/> semantics.
    /// </summary>
    public ValueTask DisposeAsync() => new ValueTask(_disposeTask.Value);

    public void Dispose() { }
}
