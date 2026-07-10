using System.Diagnostics;
using lucia.Wyoming.Audio;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SherpaOnnx;

namespace lucia.Wyoming.Stt;

/// <summary>
/// Hybrid STT session: accumulates audio in a growing buffer and re-transcribes
/// the full buffer through an offline model on a periodic cadence, producing
/// progressively refined partial transcripts in near-real-time.
///
/// Re-transcription runs on a background thread to avoid blocking the audio
/// ingestion path. <see cref="DisposeAsync"/> awaits the complete active finalization —
/// both any in-progress progressive background transcription AND the final synchronous
/// decode pass inside <see cref="GetFinalResultAsync"/> — so the STT concurrency slot
/// is only released after ALL inference for this session has genuinely stopped.
/// <see cref="GetFinalResultAsync"/> and <see cref="DisposeCore"/> share a
/// <see cref="_disposeLock"/>-protected <see cref="_finalizationTask"/> to serialize
/// "finalization started" and "disposal started", preventing an early slot release even
/// when <see cref="DisposeAsync"/> races with the final synchronous decode pass.
///
/// <see cref="AcceptAudioChunk"/> also acquires <see cref="_disposeLock"/> before
/// publishing a new progressive <see cref="_pendingTranscription"/> task: the disposed
/// re-check and the <c>Task.Run</c> enqueue are atomic under the lock. The threadpool
/// may begin executing <c>RunTranscription</c> concurrently before the lock is released,
/// but <see cref="DisposeCore"/> captures and awaits <see cref="_pendingTranscription"/>
/// (published before the lock is released) so it always awaits any inference scheduled
/// by <see cref="AcceptAudioChunk"/> — preventing early permit release.
/// </summary>
public sealed class HybridSttSession : ISttSession, IAsyncDisposable
{
    private readonly OfflineRecognizer? _recognizer;
    private readonly int _modelSampleRate;
    private readonly int _refreshIntervalSamples;
    private readonly int _minAudioSamples;
    private readonly int _progressiveThresholdSamples;
    private readonly int _maxContextSamples;
    private readonly ILogger _logger;

    private readonly List<float> _audioBuffer = [];
    private readonly object _bufferLock = new();
    private int _samplesSinceLastRefresh;
    private int _stableCount;
    private volatile string _latestTranscript = string.Empty;
    private volatile int _transcriptionCount;
    private Task? _pendingTranscription;
    private volatile bool _disposed;
    private readonly Lazy<Task> _disposeTask;
    private bool _inputFinished;
    // Serializes "finalization started" (GetFinalResultAsync) with "disposal started" (DisposeCore)
    // so DisposeCore can find and await the full finalization task, including the final sync decode.
    private readonly object _disposeLock = new();
    private Task<SttResult>? _finalizationTask;

    // Test seams — null in production; set only by HybridSttSession.CreateForTest callers.
    // BeforeProgressivePublishSeam is called BEFORE _disposeLock is acquired (the race window).
    // AfterProgressivePublishSeam is called inside the lock AFTER Task.Run is enqueued.
    internal Action? BeforeProgressivePublishSeam;
    internal Action? AfterProgressivePublishSeam;

    public HybridSttSession(
        OfflineRecognizer recognizer,
        int modelSampleRate,
        int refreshIntervalMs,
        int minAudioMs,
        double maxContextSeconds,
        double progressiveThresholdSeconds,
        ILogger logger)
        : this(
            recognizer ?? throw new ArgumentNullException(nameof(recognizer)),
            modelSampleRate, refreshIntervalMs, minAudioMs,
            maxContextSeconds, progressiveThresholdSeconds,
            logger ?? throw new ArgumentNullException(nameof(logger)),
            forTestOnly: false) { }

    /// <summary>
    /// Creates a <see cref="HybridSttSession"/> for testing without a real
    /// <see cref="OfflineRecognizer"/>. <see cref="RunTranscription"/> is a no-op when
    /// no recognizer is provided; tests use <see cref="BeforeProgressivePublishSeam"/> and
    /// <see cref="AfterProgressivePublishSeam"/> to probe the disposal race window.
    /// </summary>
    internal static HybridSttSession CreateForTest(
        int modelSampleRate = 16_000,
        int refreshIntervalMs = 1,
        int minAudioMs = 0,
        double maxContextSeconds = 0,
        double progressiveThresholdSeconds = 0.001,
        ILogger? logger = null)
        => new(null, modelSampleRate, refreshIntervalMs, minAudioMs,
               maxContextSeconds, progressiveThresholdSeconds,
               logger ?? NullLogger.Instance, forTestOnly: true);

    // Private constructor: accepts null recognizer (test instances only).
    // The trailing bool discriminator differentiates this overload from the public constructor
    // at the IL level — C# nullability (OfflineRecognizer vs OfflineRecognizer?) is compile-time
    // only and both map to the same runtime type.
    private HybridSttSession(
        OfflineRecognizer? recognizer,
        int modelSampleRate,
        int refreshIntervalMs,
        int minAudioMs,
        double maxContextSeconds,
        double progressiveThresholdSeconds,
        ILogger logger,
        bool forTestOnly)
    {
        _recognizer = recognizer;
        _modelSampleRate = modelSampleRate;
        _refreshIntervalSamples = refreshIntervalMs * modelSampleRate / 1000;
        _minAudioSamples = minAudioMs * modelSampleRate / 1000;
        _progressiveThresholdSamples = (int)(progressiveThresholdSeconds * modelSampleRate);
        _maxContextSamples = maxContextSeconds > 0
            ? (int)(maxContextSeconds * modelSampleRate)
            : int.MaxValue;
        _logger = logger;
        _disposeTask = new Lazy<Task>(DisposeCore, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public bool IsEndOfUtterance => _inputFinished;

    public void AcceptAudioChunk(ReadOnlySpan<float> samples, int sampleRate)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_inputFinished)
            throw new InvalidOperationException("Cannot accept audio after finalization.");
        if (samples.IsEmpty) return;

        float[] resampled;
        if (sampleRate != _modelSampleRate)
            resampled = AudioResampler.Resample(samples, sampleRate, _modelSampleRate);
        else
            resampled = samples.ToArray();

        lock (_bufferLock)
        {
            _audioBuffer.AddRange(resampled);
            _samplesSinceLastRefresh += resampled.Length;

            if (_audioBuffer.Count > _maxContextSamples)
            {
                var excess = _audioBuffer.Count - _maxContextSamples;
                _audioBuffer.RemoveRange(0, excess);
            }
        }

        // Progressive re-transcription only for long utterances (>5s).
        // Short HA commands (1-5s) get a single transcription at GetFinalResult.
        // Also stop progressive updates if the transcript has stabilized (same result 2x in a row).
        if (_samplesSinceLastRefresh >= _refreshIntervalSamples
            && _stableCount < 2
            && (_pendingTranscription is null || _pendingTranscription.IsCompleted))
        {
            int bufferCount;
            lock (_bufferLock) { bufferCount = _audioBuffer.Count; }

            if (bufferCount >= _progressiveThresholdSamples)
            {
                // Test seam: fires BEFORE the lock, placing execution in the race window
                // (between the fast-path _disposed check above and the lock acquisition below).
                // Null in production; set only by CreateForTest callers.
                BeforeProgressivePublishSeam?.Invoke();

                // Serialize the disposed re-check and publish under _disposeLock: the _disposed
                // check and the _pendingTranscription assignment are atomic under this lock.
                // Task.Run ENQUEUES the work inside the lock; the threadpool MAY begin executing
                // RunTranscription on another thread before the lock block exits, but that is
                // safe — DisposeCore captures and awaits _pendingTranscription (published before
                // the lock releases) so it always awaits any inference scheduled here.
                // Without this lock, DisposeCore could complete (seeing _pendingTranscription as
                // null) and release the STT permit before Task.Run fires.
                lock (_disposeLock)
                {
                    if (_disposed) return;
                    _samplesSinceLastRefresh = 0;
                    _pendingTranscription = Task.Run(RunTranscription);
                    // Test seam: fires inside the lock AFTER Task.Run; signals that the progressive
                    // publish happened. If this fires post-disposal, the _disposed guard was bypassed.
                    AfterProgressivePublishSeam?.Invoke();
                }
            }
        }
    }

    public SttResult GetPartialResult()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new SttResult
        {
            Text = _latestTranscript,
            Confidence = _transcriptionCount > 0 ? 0.5f : 0f,
        };
    }

    /// <summary>
    /// Returns the finalization task, creating it on first call. The task covers BOTH
    /// the progressive background transcription wait AND the final synchronous decode pass,
    /// so <see cref="DisposeCore"/> can await it to guarantee all inference has stopped.
    /// Thread-safe: concurrent calls return the same task under <see cref="_disposeLock"/>.
    /// </summary>
    public Task<SttResult> GetFinalResultAsync()
    {
        lock (_disposeLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_finalizationTask is null)
            {
                _inputFinished = true;
                // FinalizeCore() starts synchronously up to its first await.
                // The lock is held while FinalizeCore runs its synchronous prefix,
                // which prevents DisposeCore from reading _finalizationTask as null
                // while the final decode is already executing.
                _finalizationTask = FinalizeCore();
            }
            return _finalizationTask;
        }
    }

    private async Task<SttResult> FinalizeCore()
    {
        // Await any in-progress progressive background transcription first.
        if (_pendingTranscription is { IsCompleted: false } pending)
        {
            try { await pending.ConfigureAwait(false); }
            catch { /* fault in progressive pass — proceed with accumulated transcript */ }
        }

        // Final synchronous decode on the complete buffer.
        int bufferCount;
        lock (_bufferLock) { bufferCount = _audioBuffer.Count; }
        if (bufferCount > 0)
            RunTranscription();

        return new SttResult
        {
            Text = _latestTranscript,
            Confidence = _transcriptionCount > 0 ? 1.0f : 0f,
        };
    }

    /// <summary>
    /// Triggers the shared disposal task (non-blocking). Inference may still be running;
    /// use <see cref="DisposeAsync"/> to await completion.
    /// </summary>
    public void Dispose() => _ = _disposeTask.Value;

    /// <summary>
    /// Returns the shared disposal-completion task. All concurrent callers await the SAME
    /// task so no caller returns until the pending background transcription has finished.
    /// </summary>
    public ValueTask DisposeAsync() => new ValueTask(_disposeTask.Value);

    private async Task DisposeCore()
    {
        Task<SttResult>? finTask;
        lock (_disposeLock)
        {
            _disposed = true;
            // Capture whatever finalization was started (may be null if AudioStop never arrived).
            finTask = _finalizationTask;
        }

        if (finTask is not null)
        {
            // Await the full finalization: covers both _pendingTranscription AND the final decode.
            // If it already completed, this returns immediately (no overhead).
            try { await finTask.ConfigureAwait(false); }
            catch { /* fault during finalization — slot still released by caller's finally */ }
            return;
        }

        // No finalization was started; still await any running progressive background task.
        if (_pendingTranscription is { IsCompleted: false } pending)
        {
            try { await pending.ConfigureAwait(false); }
            catch { }
        }
    }

    private void RunTranscription()
    {
        // Test instances (created via CreateForTest) have null _recognizer; no-op safely.
        if (_recognizer is null) return;

        var sw = Stopwatch.StartNew();

        float[] audio;
        lock (_bufferLock)
        {
            audio = _audioBuffer.ToArray();
        }

        using var stream = _recognizer.CreateStream();
        stream.AcceptWaveform(_modelSampleRate, audio);
        _recognizer.Decode(stream);

        var text = stream.Result.Text.Trim();
        sw.Stop();
        _transcriptionCount++;

        var bufferMs = audio.Length * 1000 / _modelSampleRate;
        var changed = !string.Equals(_latestTranscript, text, StringComparison.Ordinal);
        _latestTranscript = text;

        // Track stability — stop progressive updates if result hasn't changed
        _stableCount = changed ? 0 : _stableCount + 1;

        _logger.LogDebug(
            "Hybrid STT #{Count}: {BufferMs}ms audio \u2192 \"{Text}\" in {InferenceMs}ms{Changed}",
            _transcriptionCount, bufferMs, text, sw.ElapsedMilliseconds,
            changed ? " (updated)" : "");
    }
}
