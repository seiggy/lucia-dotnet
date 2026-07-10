using lucia.Wyoming.Stt;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucia.Tests.Wyoming;

/// <summary>
/// Unit tests for <see cref="HybridSttSession"/> that exercise the REAL production code
/// (not a mock) via its internal test seams.
/// </summary>
public sealed class HybridSttSessionDisposalRaceTests
{
    /// <summary>
    /// Deterministic regression for the AcceptAudioChunk/DisposeCore disposal race (Vasquez round 9).
    ///
    /// Race scenario without the <c>_disposeLock</c> guard:
    ///   1. AcceptAudioChunk passes the fast-path <c>ObjectDisposedException</c> check.
    ///   2. DisposeCore runs: acquires <c>_disposeLock</c>, sets <c>_disposed=true</c>,
    ///      observes <c>_pendingTranscription==null</c> (not yet published), returns.
    ///   3. AcceptAudioChunk calls <c>Task.Run(RunTranscription)</c> — inference runs
    ///      after the STT permit has been released, exceeding MaxConcurrentSttSessions.
    ///
    /// How the test works:
    ///   - <see cref="HybridSttSession.BeforeProgressivePublishSeam"/> blocks AcceptAudioChunk
    ///     BEFORE it acquires <c>_disposeLock</c>, placing it in the exact race window.
    ///   - The test awaits DisposeAsync (disposal wins the race, <c>_disposed=true</c>).
    ///   - The gate is released; AcceptAudioChunk enters the lock, sees <c>_disposed=true</c>,
    ///     and returns WITHOUT calling <c>Task.Run</c>.
    ///   - <see cref="HybridSttSession.AfterProgressivePublishSeam"/> is never invoked
    ///     → assert publish count is 0.
    ///
    /// This test FAILS if the <c>if (_disposed) return;</c> guard is removed from
    /// AcceptAudioChunk's <c>_disposeLock</c> block (AfterProgressivePublishSeam fires → count=1).
    ///
    /// Uses <see cref="HybridSttSession.CreateForTest"/> (null recognizer); RunTranscription
    /// no-ops safely so no real ONNX model is required.
    /// </summary>
    [Fact]
    public async Task AcceptAudioChunk_ProgressivePublish_IsDroppedWhenDisposalWinsRace()
    {
        var seamReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new ManualResetEventSlim(initialState: false);
        var publishCount = 0;

        using var session = HybridSttSession.CreateForTest(
            modelSampleRate: 16_000,
            refreshIntervalMs: 1,             // _refreshIntervalSamples = 16 samples
            minAudioMs: 0,
            maxContextSeconds: 0,
            progressiveThresholdSeconds: 0.001); // _progressiveThresholdSamples = 16 samples

        // BeforeProgressivePublishSeam: fires BEFORE _disposeLock is acquired (race window).
        // Signals seamReached (deterministic) then blocks until the test releases the gate.
        session.BeforeProgressivePublishSeam = () =>
        {
            seamReached.TrySetResult();
            gate.Wait();
        };

        // AfterProgressivePublishSeam: fires inside the lock AFTER Task.Run(RunTranscription).
        // Increments publishCount — if > 0, the _disposed guard was bypassed (bug reproduced).
        session.AfterProgressivePublishSeam = () => Interlocked.Increment(ref publishCount);

        // Feed 20 samples: enough to satisfy _refreshIntervalSamples (16) and
        // _progressiveThresholdSamples (16), triggering progressive transcription.
        // Static local avoids ReadOnlySpan<T> ref-struct capture in the Task.Run lambda.
        static void Feed(HybridSttSession s, float[] a) => s.AcceptAudioChunk(a, 16_000);
        var samples = new float[20];

        // AcceptAudioChunk will pass all outer conditions, then call BeforePublishSeam and block.
        var acceptTask = Task.Run(() => Feed(session, samples));

        // Deterministic wait: AcceptAudioChunk has signalled that it is at the race window.
        await seamReached.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Run DisposeAsync to completion (disposal wins the race):
        // DisposeCore acquires _disposeLock, sets _disposed=true, sees _pendingTranscription==null
        // (AcceptAudioChunk hasn't published yet), and returns.
        await session.DisposeAsync();

        // Release AcceptAudioChunk. With the production fix, it enters the lock, sees _disposed=true,
        // and returns WITHOUT calling Task.Run — so AfterProgressivePublishSeam is never invoked.
        gate.Set();
        await acceptTask;

        // ASSERT: the _disposeLock guard prevented the post-disposal publish.
        // Fails to 1 if `if (_disposed) return;` is removed from AcceptAudioChunk's lock block.
        Assert.Equal(0, publishCount);
    }
}
