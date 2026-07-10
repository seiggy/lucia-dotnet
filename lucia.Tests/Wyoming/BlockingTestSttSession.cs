using lucia.Wyoming.Stt;

namespace lucia.Tests.Wyoming;

/// <summary>
/// A test STT session that blocks specifically inside the <em>final inference</em> phase of
/// <see cref="GetFinalResultAsync"/> — i.e., after any background progressive transcription
/// would have completed, modelling <see cref="HybridSttSession"/>'s final <c>RunTranscription()</c>
/// synchronous pass.  <see cref="InferenceStarted"/> fires when that phase starts so tests can
/// synchronise on "we are provably inside final inference"; <see cref="Unblock"/> lets it finish.
///
/// <see cref="DisposeAsync"/> returns a <see cref="ValueTask"/> backed by the same shared
/// finalization <see cref="Task{T}"/> created by <see cref="GetFinalResultAsync"/>, matching the
/// invariant that <see cref="WyomingSession"/> must not release the STT slot until the full
/// finalization (including the final decode pass) has completed.
/// </summary>
internal sealed class BlockingTestSttSession : ISttSession, IAsyncDisposable
{
    private readonly SttResult _result;
    // Gates the final-inference phase. Unblock() fires this, completing FinalizeAsync.
    private readonly TaskCompletionSource _finalInferenceGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    // Lock serialises "finalization started" (GetFinalResultAsync) with "read finalization"
    // (DisposeAsync), mirroring HybridSttSession._disposeLock semantics.
    private readonly object _finalizationLock = new();
    private Task<SttResult>? _finalizationTask;

    public BlockingTestSttSession(SttResult result) => _result = result;

    /// <summary>Fires when <see cref="GetFinalResultAsync"/> enters the final-inference gate.</summary>
    public TaskCompletionSource InferenceStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Unblocks the final-inference gate, allowing <see cref="GetFinalResultAsync"/> to return.</summary>
    public void Unblock() => _finalInferenceGate.TrySetResult();

    public bool IsEndOfUtterance => false;
    public void AcceptAudioChunk(ReadOnlySpan<float> samples, int sampleRate) { }
    public SttResult GetPartialResult() => new();

    /// <summary>
    /// Returns the shared finalization task (created on first call, idempotent thereafter).
    /// The task fires <see cref="InferenceStarted"/> then blocks until <see cref="Unblock"/>.
    /// </summary>
    public Task<SttResult> GetFinalResultAsync()
    {
        lock (_finalizationLock)
        {
            if (_finalizationTask is null)
                _finalizationTask = FinalizeAsync();
            return _finalizationTask;
        }
    }

    private async Task<SttResult> FinalizeAsync()
    {
        InferenceStarted.TrySetResult();     // signal: final inference is now running
        await _finalInferenceGate.Task;      // block: simulates RunTranscription()
        return _result;
    }

    /// <summary>
    /// Returns a <see cref="ValueTask"/> backed by the shared finalization task if it has been
    /// started, or <see cref="ValueTask.CompletedTask"/> if <see cref="GetFinalResultAsync"/>
    /// was never called. This means <c>DisposeCurrentSttSession</c> blocks on the gate if and
    /// only if final inference is actually in progress — the invariant the production code enforces.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        Task<SttResult>? finTask;
        lock (_finalizationLock)
        {
            finTask = _finalizationTask;
        }
        return finTask is not null ? new ValueTask(finTask) : ValueTask.CompletedTask;
    }

    public void Dispose() { }
}
