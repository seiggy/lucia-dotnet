using lucia.Wyoming.Stt;

namespace lucia.Tests.Wyoming;

/// <summary>
/// Test <see cref="ISttSession"/> that models the <c>HybridSttSession.AcceptAudioChunk</c>
/// disposal race: <see cref="AcceptAudioChunk"/> signals <see cref="AcceptReachedGate"/>
/// and then blocks on an internal gate, representing the window between the fast-path
/// <see cref="ObjectDisposedException"/> check and the <c>_disposeLock</c>-protected
/// progressive-publish. After the gate is released, <see cref="AcceptAudioChunk"/> acquires
/// the same lock used by <see cref="DisposeAsync"/>, re-checks <c>_disposed</c>, and
/// increments <see cref="InferenceScheduledCount"/> only if the session is NOT yet disposed.
///
/// This mirrors the <c>_disposeLock</c> fix applied to <c>HybridSttSession</c>.
/// Without that lock re-check, <see cref="InferenceScheduledCount"/> would be positive
/// even after <see cref="DisposeAsync"/> completes — the race that the fix prevents.
/// </summary>
internal sealed class GatableAcceptSttSession : ISttSession, IAsyncDisposable
{
    private readonly object _lock = new();
    private bool _disposed;
    private readonly ManualResetEventSlim _gate = new(initialState: false);
    private volatile int _inferenceScheduledCount;

    /// <summary>Fires when <see cref="AcceptAudioChunk"/> reaches the gate (disposed-check boundary).</summary>
    public TaskCompletionSource AcceptReachedGate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Releases the gate, allowing <see cref="AcceptAudioChunk"/> to proceed to the lock re-check.</summary>
    public void ReleaseGate() => _gate.Set();

    /// <summary>
    /// Number of times <see cref="AcceptAudioChunk"/> scheduled inference AFTER passing the
    /// <c>_lock</c> re-check. Must remain 0 when disposal completes before the gate is released.
    /// </summary>
    public int InferenceScheduledCount => _inferenceScheduledCount;

    public bool IsEndOfUtterance => false;

    /// <summary>
    /// Signals <see cref="AcceptReachedGate"/> (we are at the disposed-check boundary),
    /// blocks until <see cref="ReleaseGate"/> is called, then acquires <c>_lock</c> and
    /// increments <see cref="InferenceScheduledCount"/> only if not disposed — modelling
    /// the <c>_disposeLock</c>-protected publish in <c>HybridSttSession.AcceptAudioChunk</c>.
    /// </summary>
    public void AcceptAudioChunk(ReadOnlySpan<float> samples, int sampleRate)
    {
        if (samples.IsEmpty)
        {
            return;
        }

        AcceptReachedGate.TrySetResult();
        _gate.Wait();

        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            Interlocked.Increment(ref _inferenceScheduledCount);
        }
    }

    public SttResult GetPartialResult() => new();

    public Task<SttResult> GetFinalResultAsync()
        => Task.FromResult(new SttResult { Text = "gate test", Confidence = 1.0f });

    /// <summary>
    /// Marks the session disposed under <c>_lock</c> and returns immediately, modelling
    /// <c>HybridSttSession.DisposeCore</c> completing fast when no finalization is in progress.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        lock (_lock)
        {
            _disposed = true;
        }

        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
        }
    }
}
