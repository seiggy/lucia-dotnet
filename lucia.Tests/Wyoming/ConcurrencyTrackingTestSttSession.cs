using lucia.Wyoming.Stt;

namespace lucia.Tests.Wyoming;

/// <summary>
/// An <see cref="ISttSession"/> that measures peak concurrent execution of
/// <see cref="GetFinalResultAsync"/> across all sharing instances.
/// Thread-safe; uses <see cref="Interlocked"/> to avoid lock contention.
/// </summary>
internal sealed class ConcurrencyTrackingTestSttSession : ISttSession
{
    /// <summary>
    /// Shared two-element array: [0] = current concurrent count, [1] = max observed.
    /// All sessions that should be counted together must share the same array reference.
    /// </summary>
    private readonly int[] _sharedCounters;
    private readonly int _inferenceDelayMs;

    public ConcurrencyTrackingTestSttSession(int[] sharedCounters, int inferenceDelayMs = 50)
    {
        ArgumentNullException.ThrowIfNull(sharedCounters);
        if (sharedCounters.Length < 2)
            throw new ArgumentException("sharedCounters must have at least 2 elements.", nameof(sharedCounters));

        _sharedCounters = sharedCounters;
        _inferenceDelayMs = inferenceDelayMs;
    }

    public bool IsEndOfUtterance => false;
    public void AcceptAudioChunk(ReadOnlySpan<float> samples, int sampleRate) { }
    public SttResult GetPartialResult() => new();

    public async Task<SttResult> GetFinalResultAsync()
    {
        var current = Interlocked.Increment(ref _sharedCounters[0]);

        // CAS loop: update max if current exceeds recorded max.
        int observed;
        do
        {
            observed = Volatile.Read(ref _sharedCounters[1]);
        }
        while (current > observed
            && Interlocked.CompareExchange(ref _sharedCounters[1], current, observed) != observed);

        try
        {
            await Task.Delay(_inferenceDelayMs).ConfigureAwait(false);
            return new SttResult { Text = "concurrent test", Confidence = 1.0f };
        }
        finally
        {
            Interlocked.Decrement(ref _sharedCounters[0]);
        }
    }

    public void Dispose() { }
}
