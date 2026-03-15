using System.Diagnostics;
using lucia.Wyoming.Audio;
using Microsoft.Extensions.Logging;
using SherpaOnnx;

namespace lucia.Wyoming.Stt;

/// <summary>
/// Hybrid STT session: accumulates audio in a growing buffer and re-transcribes
/// the full buffer through an offline model on a periodic cadence, producing
/// progressively refined partial transcripts in near-real-time.
///
/// Re-transcription runs on a background thread to avoid blocking the audio
/// ingestion path. Only GetFinalResult() blocks until the last transcription completes.
/// </summary>
public sealed class HybridSttSession : ISttSession
{
    private readonly OfflineRecognizer _recognizer;
    private readonly int _modelSampleRate;
    private readonly int _refreshIntervalSamples;
    private readonly int _minAudioSamples;
    private readonly int _maxContextSamples;
    private readonly ILogger _logger;

    private readonly List<float> _audioBuffer = [];
    private readonly object _bufferLock = new();
    private int _samplesSinceLastRefresh;
    private volatile string _latestTranscript = string.Empty;
    private volatile int _transcriptionCount;
    private Task? _pendingTranscription;
    private bool _disposed;
    private bool _inputFinished;

    public HybridSttSession(
        OfflineRecognizer recognizer,
        int modelSampleRate,
        int refreshIntervalMs,
        int minAudioMs,
        double maxContextSeconds,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(recognizer);
        ArgumentNullException.ThrowIfNull(logger);

        _recognizer = recognizer;
        _modelSampleRate = modelSampleRate;
        _refreshIntervalSamples = refreshIntervalMs * modelSampleRate / 1000;
        _minAudioSamples = minAudioMs * modelSampleRate / 1000;
        _maxContextSamples = maxContextSeconds > 0
            ? (int)(maxContextSeconds * modelSampleRate)
            : int.MaxValue;
        _logger = logger;
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

        // Progressive re-transcription only kicks in after 5s of audio.
        // For typical HA commands (1-5s), a single transcription at GetFinalResult
        // is faster and avoids blocking the audio ingestion path.
        var progressiveThresholdSamples = 5 * _modelSampleRate;
        if (_samplesSinceLastRefresh >= _refreshIntervalSamples
            && (_pendingTranscription is null || _pendingTranscription.IsCompleted))
        {
            int bufferCount;
            lock (_bufferLock) { bufferCount = _audioBuffer.Count; }

            if (bufferCount >= progressiveThresholdSamples)
            {
                _samplesSinceLastRefresh = 0;
                _pendingTranscription = Task.Run(RunTranscription);
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

    public SttResult GetFinalResult()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _inputFinished = true;

        // Wait for any pending background transcription to finish
        _pendingTranscription?.GetAwaiter().GetResult();

        // Run one final transcription on the complete buffer
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }

    private void RunTranscription()
    {
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

        _logger.LogDebug(
            "Hybrid STT #{Count}: {BufferMs}ms audio \u2192 \"{Text}\" in {InferenceMs}ms{Changed}",
            _transcriptionCount, bufferMs, text, sw.ElapsedMilliseconds,
            changed ? " (updated)" : "");
    }
}
