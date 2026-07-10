using lucia.Wyoming.Stt;

namespace lucia.Tests.Wyoming;

/// <summary>
/// A test STT session whose <see cref="GetFinalResultAsync"/> blocks until <see cref="Unblock"/> is called.
/// Useful for testing semaphore release behaviour during shutdown.
/// </summary>
internal sealed class BlockingTestSttSession : ISttSession
{
    private readonly SttResult _result;
    private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public BlockingTestSttSession(SttResult result) => _result = result;

    /// <summary>Called when <see cref="GetFinalResultAsync"/> is invoked — lets tests synchronize on this point.</summary>
    public TaskCompletionSource InferenceStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Unblocks the pending <see cref="GetFinalResultAsync"/> call.</summary>
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

    public void Dispose() { }
}
