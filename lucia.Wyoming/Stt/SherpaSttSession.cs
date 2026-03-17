using lucia.Wyoming.Audio;
using SherpaOnnx;

namespace lucia.Wyoming.Stt;

public sealed class SherpaSttSession : ISttSession
{
    private readonly OnlineRecognizer _recognizer;
    private readonly OnlineStream _stream;
    private readonly int _modelSampleRate;
    private bool _disposed;
    private bool _inputFinished;

    public SherpaSttSession(OnlineRecognizer recognizer, OnlineStream stream, int modelSampleRate)
    {
        ArgumentNullException.ThrowIfNull(recognizer);
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(modelSampleRate);

        _recognizer = recognizer;
        _stream = stream;
        _modelSampleRate = modelSampleRate;
    }

    public bool IsEndOfUtterance
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _recognizer.IsEndpoint(_stream);
        }
    }

    public void AcceptAudioChunk(ReadOnlySpan<float> samples, int sampleRate)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);

        if (_inputFinished)
        {
            throw new InvalidOperationException("Cannot accept audio after finalization.");
        }

        if (samples.IsEmpty)
        {
            return;
        }

        if (sampleRate != _modelSampleRate)
        {
            var resampled = AudioResampler.Resample(samples, sampleRate, _modelSampleRate);
            _stream.AcceptWaveform(_modelSampleRate, resampled);
        }
        else
        {
            _stream.AcceptWaveform(sampleRate, samples.ToArray());
        }

        DecodeAvailable();
    }

    public SttResult GetPartialResult()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        DecodeAvailable();

        return new SttResult
        {
            Text = _recognizer.GetResult(_stream).Text,
            Confidence = 0.0f,
        };
    }

    public Task<SttResult> GetFinalResultAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_inputFinished)
        {
            _stream.InputFinished();
            _inputFinished = true;
        }

        DecodeAvailable();

        return Task.FromResult(new SttResult
        {
            Text = _recognizer.GetResult(_stream).Text.Trim(),
            Confidence = 1.0f,
        });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stream.Dispose();
    }

    private void DecodeAvailable()
    {
        while (_recognizer.IsReady(_stream))
        {
            _recognizer.Decode(_stream);
        }
    }
}
