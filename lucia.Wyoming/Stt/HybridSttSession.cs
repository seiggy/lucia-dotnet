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
/// For a typical 2-3 second home automation command with Parakeet TDT 0.6B (~68ms
/// per full-buffer inference), this yields 5-7 progressive updates during the utterance.
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
    private int _samplesSinceLastRefresh;
    private string _latestTranscript = string.Empty;
    private int _transcriptionCount;
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

        // Resample if needed
        float[] resampled;
        if (sampleRate != _modelSampleRate)
        {
            resampled = AudioResampler.Resample(samples, sampleRate, _modelSampleRate);
        }
        else
        {
            resampled = samples.ToArray();
        }

        _audioBuffer.AddRange(resampled);
        _samplesSinceLastRefresh += resampled.Length;

        // Enforce max context window: trim old audio if buffer exceeds limit
        if (_audioBuffer.Count > _maxContextSamples)
        {
            var excess = _audioBuffer.Count - _maxContextSamples;
            _audioBuffer.RemoveRange(0, excess);
        }

        // Trigger re-transcription based on audio time, not wall-clock time
        if (_audioBuffer.Count >= _minAudioSamples
            && _samplesSinceLastRefresh >= _refreshIntervalSamples)
        {
            RunTranscription();
            _samplesSinceLastRefresh = 0;
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

        // Run one final transcription on the complete buffer
        if (_audioBuffer.Count > 0)
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
        // OfflineRecognizer is shared — don't dispose it here
    }

    private void RunTranscription()
    {
        var sw = Stopwatch.StartNew();

        var audio = _audioBuffer.ToArray();
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
