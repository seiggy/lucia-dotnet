using lucia.Wyoming.Audio;
using SherpaOnnx;

namespace lucia.Wyoming.WakeWord;

public sealed class SherpaWakeWordSession : IWakeWordSession
{
    private const int TargetSampleRate = 16000;

    private readonly KeywordSpotter _spotter;
    private readonly OnlineStream _stream;
    private bool _disposed;

    public SherpaWakeWordSession(KeywordSpotter spotter, OnlineStream stream)
    {
        _spotter = spotter;
        _stream = stream;
    }

    public void AcceptAudioChunk(ReadOnlySpan<float> samples, int sampleRate)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);

        if (samples.IsEmpty)
        {
            return;
        }

        var normalizedSamples = sampleRate == TargetSampleRate
            ? samples.ToArray()
            : AudioResampler.Resample(samples, sampleRate, TargetSampleRate);

        _stream.AcceptWaveform(TargetSampleRate, normalizedSamples);
    }

    public WakeWordResult? CheckForDetection()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        while (_spotter.IsReady(_stream))
        {
            _spotter.Decode(_stream);
        }

        var result = _spotter.GetResult(_stream);
        if (string.IsNullOrWhiteSpace(result.Keyword))
        {
            return null;
        }

        return new WakeWordResult
        {
            Keyword = result.Keyword,
            Confidence = 1.0f,
            Timestamp = DateTimeOffset.UtcNow,
        };
    }

    public void Reset()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _spotter.Reset(_stream);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _stream.Dispose();
        _disposed = true;
    }
}
