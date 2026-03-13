using SherpaOnnx;

namespace lucia.Wyoming.Vad;

public sealed class SherpaVadSession : IVadSession
{
    private readonly VoiceActivityDetector _vad;
    private readonly Queue<VadSegment> _segments = new();
    private readonly int _sampleRate;
    private bool _disposed;

    public SherpaVadSession(VadModelConfig config, int sampleRate)
    {
        _sampleRate = sampleRate;
        _vad = new VoiceActivityDetector(config, bufferSizeInSeconds: 30);
    }

    public bool HasSpeechSegment => _segments.Count > 0;

    public void AcceptAudioChunk(ReadOnlySpan<float> samples)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (samples.IsEmpty)
        {
            return;
        }

        _vad.AcceptWaveform(samples.ToArray());
        DrainSegments();
    }

    public void Flush()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _vad.Flush();
        DrainSegments();
    }

    public VadSegment GetNextSegment()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return _segments.Count == 0
            ? throw new InvalidOperationException("No speech segment available")
            : _segments.Dequeue();
    }

    public void Reset()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _vad.Reset();
        _segments.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _vad.Dispose();
    }

    private void DrainSegments()
    {
        while (!_vad.IsEmpty())
        {
            var segment = _vad.Front();

            _segments.Enqueue(new VadSegment
            {
                Samples = segment.Samples,
                StartTime = TimeSpan.FromSeconds(segment.Start / (double)_sampleRate),
                EndTime = TimeSpan.FromSeconds(
                    segment.Start / (double)_sampleRate + segment.Samples.Length / (double)_sampleRate),
                SampleRate = _sampleRate,
            });

            _vad.Pop();
        }
    }
}
