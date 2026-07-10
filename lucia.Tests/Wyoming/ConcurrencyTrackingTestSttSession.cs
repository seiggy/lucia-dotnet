using lucia.Wyoming.Stt;

namespace lucia.Tests.Wyoming;

/// <summary>
/// An <see cref="ISttSession"/> that measures peak concurrent background STT work across
/// all sharing instances, modelling <see cref="HybridSttSession"/>'s behaviour where
/// <c>AcceptAudioChunk</c> starts a background decode task that must complete before the
/// concurrency slot can be safely released.
///
/// Counter semantics: incremented on first <c>AcceptAudioChunk</c> (decode work starts),
/// decremented only after <c>DisposeAsync</c> returns (background decode finished).
/// This means the counter stays high until <see cref="DisposeCurrentSttSessionAsync"/>
/// completes, validating that the slot is not returned while work is still in flight.
///
/// Thread-safe; uses <see cref="Interlocked"/> to avoid lock contention.
/// </summary>
internal sealed class ConcurrencyTrackingTestSttSession : ISttSession, IAsyncDisposable
{
    /// <summary>
    /// Shared two-element array: [0] = current concurrent count, [1] = max observed.
    /// All sessions that should be counted together must share the same array reference.
    /// </summary>
    private readonly int[] _sharedCounters;
    private readonly int _inferenceDelayMs;
    private bool _workStarted;
    private int _disposeState;  // 0 = active, 1 = disposed (idempotency guard)

    public ConcurrencyTrackingTestSttSession(int[] sharedCounters, int inferenceDelayMs = 50)
    {
        ArgumentNullException.ThrowIfNull(sharedCounters);
        if (sharedCounters.Length < 2)
            throw new ArgumentException("sharedCounters must have at least 2 elements.", nameof(sharedCounters));

        _sharedCounters = sharedCounters;
        _inferenceDelayMs = inferenceDelayMs;
    }

    public bool IsEndOfUtterance => false;

    public void AcceptAudioChunk(ReadOnlySpan<float> samples, int sampleRate)
    {
        if (_workStarted) return;
        _workStarted = true;

        // Simulate a background RunTranscription task starting (like HybridSttSession).
        // The counter stays elevated until DisposeAsync, so tests can verify the slot is
        // held for the full duration of background work, not just GetFinalResultAsync.
        var current = Interlocked.Increment(ref _sharedCounters[0]);

        int observed;
        do
        {
            observed = Volatile.Read(ref _sharedCounters[1]);
        }
        while (current > observed
            && Interlocked.CompareExchange(ref _sharedCounters[1], current, observed) != observed);
    }

    public SttResult GetPartialResult() => new();

    /// <summary>Returns the transcript immediately — inference time is modelled in DisposeAsync.</summary>
    public Task<SttResult> GetFinalResultAsync()
        => Task.FromResult(new SttResult { Text = "concurrent test", Confidence = 1.0f });

    /// <summary>
    /// Simulates awaiting a pending background transcription task (like
    /// <c>HybridSttSession.DisposeAsync</c>) before signalling that work is done.
    /// The slot must NOT be released until this method returns.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0) return;
        if (_workStarted)
        {
            await Task.Delay(_inferenceDelayMs).ConfigureAwait(false);
            Interlocked.Decrement(ref _sharedCounters[0]);
        }
    }

    /// <summary>Sync disposal — used only from non-async teardown paths; decrements immediately.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0) return;
        if (_workStarted)
            Interlocked.Decrement(ref _sharedCounters[0]);
    }
}
