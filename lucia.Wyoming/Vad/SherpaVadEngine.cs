using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SherpaOnnx;

namespace lucia.Wyoming.Vad;

public sealed class SherpaVadEngine : IVadEngine
{
    private readonly ILogger<SherpaVadEngine> _logger;
    private readonly VadModelConfig? _config;
    private readonly int _sampleRate;

    public SherpaVadEngine(
        IOptions<VadOptions> options,
        ILogger<SherpaVadEngine> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;

        var opts = options.Value;
        _sampleRate = opts.SampleRate;

        try
        {
            _config = BuildConfig(opts);
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

    public IVadSession CreateSession()
    {
        if (!IsReady || _config is null)
        {
            throw new InvalidOperationException("VAD engine is not ready.");
        }

        return new SherpaVadSession(_config.Value, _sampleRate);
    }

    public void Dispose()
    {
    }

    private static VadModelConfig BuildConfig(VadOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.ModelPath))
        {
            throw new InvalidOperationException("VAD model path must be configured.");
        }

        if (!File.Exists(options.ModelPath))
        {
            throw new FileNotFoundException(
                $"VAD model file '{options.ModelPath}' was not found.",
                options.ModelPath);
        }

        return new VadModelConfig
        {
            SileroVad = new SileroVadModelConfig
            {
                Model = options.ModelPath,
                Threshold = options.Threshold,
                MinSpeechDuration = options.MinSpeechDuration,
                MinSilenceDuration = options.MinSilenceDuration,
            },
            SampleRate = options.SampleRate,
        };
    }
}
