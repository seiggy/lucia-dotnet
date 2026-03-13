using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SherpaOnnx;

namespace lucia.Wyoming.Vad;

public sealed class SherpaVadEngine : IVadEngine
{
    private readonly VoiceActivityDetector? _vad;
    private readonly ILogger<SherpaVadEngine> _logger;
    private readonly Queue<VadSegment> _segments = new();
    private readonly int _sampleRate;

    public SherpaVadEngine(
        IOptions<VadOptions> options,
        ILogger<SherpaVadEngine> logger)
    {
        _logger = logger;

        var opts = options.Value;
        _sampleRate = opts.SampleRate;

        try
        {
            var config = new SileroVadModelConfig
            {
                Model = opts.ModelPath,
                Threshold = opts.Threshold,
                MinSpeechDuration = opts.MinSpeechDuration,
                MinSilenceDuration = opts.MinSilenceDuration,
            };

            var vadConfig = new VadModelConfig
            {
                SileroVad = config,
                SampleRate = opts.SampleRate,
            };

            _vad = new VoiceActivityDetector(vadConfig, bufferSizeInSeconds: 30);
            IsReady = true;

            _logger.LogInformation(
                "Sherpa VAD engine initialized with model at {Path}",
                opts.ModelPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Sherpa VAD engine");
            IsReady = false;
        }
    }

    public bool IsReady { get; }

    public bool HasSpeechSegment => _segments.Count > 0;

    public void AcceptAudioChunk(ReadOnlySpan<float> samples)
    {
        if (_vad is null || samples.IsEmpty)
        {
            return;
        }

        _vad.AcceptWaveform(samples.ToArray());

        while (!_vad.IsEmpty())
        {
            var segment = _vad.Front();
            var start = TimeSpan.FromSeconds(segment.Start / (double)_sampleRate);
            var end = TimeSpan.FromSeconds((segment.Start + segment.Samples.Length) / (double)_sampleRate);

            _segments.Enqueue(new VadSegment
            {
                Samples = segment.Samples,
                Start = start,
                End = end,
            });

            _vad.Pop();
        }
    }

    public VadSegment GetNextSegment()
    {
        if (_segments.Count == 0)
        {
            throw new InvalidOperationException("No speech segment available");
        }

        return _segments.Dequeue();
    }

    public void Reset()
    {
        _vad?.Reset();
        _segments.Clear();
    }

    public void Dispose()
    {
        _vad?.Dispose();
    }
}
