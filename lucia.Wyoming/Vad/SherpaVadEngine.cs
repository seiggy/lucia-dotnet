using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SherpaOnnx;
using lucia.Wyoming.Models;

namespace lucia.Wyoming.Vad;

public sealed class SherpaVadEngine : IVadEngine, IDisposable
{
    private readonly ILogger<SherpaVadEngine> _logger;
    private readonly IModelChangeNotifier _modelChangeNotifier;
    private readonly object _lock = new();
    private readonly VadOptions _options;
    private readonly int _sampleRate;
    private VadModelConfig? _config;

    public SherpaVadEngine(
        IOptions<VadOptions> options,
        IModelChangeNotifier modelChangeNotifier,
        ILogger<SherpaVadEngine> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(modelChangeNotifier);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options.Value;
        _sampleRate = _options.SampleRate;
        _logger = logger;
        _modelChangeNotifier = modelChangeNotifier;

        TryLoadModel(_options.ModelPath);
        _modelChangeNotifier.ActiveModelChanged += OnActiveModelChanged;
    }

    public bool IsReady { get; private set; }

    public IVadSession CreateSession()
    {
        VadModelConfig config;
        lock (_lock)
        {
            if (!IsReady || _config is null)
            {
                throw new InvalidOperationException("VAD engine is not ready.");
            }

            config = _config.Value;
        }

        return new SherpaVadSession(config, _sampleRate);
    }

    public void Dispose()
    {
        _modelChangeNotifier.ActiveModelChanged -= OnActiveModelChanged;

        lock (_lock)
        {
            IsReady = false;
            _config = null;
        }
    }

    private void OnActiveModelChanged(ActiveModelChangedEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);

        if (evt.EngineType != EngineType.Vad) return;

        _logger.LogInformation("Reloading VAD engine with model {ModelId}", evt.ModelId);
        TryLoadModel(evt.ModelPath);
    }

    private void TryLoadModel(string modelPath)
    {
        var onnxPath = ResolveModelFile(modelPath);
        if (string.IsNullOrWhiteSpace(onnxPath))
        {
            _logger.LogWarning("VAD model path not configured or missing: {ModelPath}", modelPath);
            lock (_lock)
            {
                IsReady = false;
            }

            return;
        }

        lock (_lock)
        {
            try
            {
                _config = BuildConfig(onnxPath, _options);
                IsReady = true;

                _logger.LogInformation(
                    "Sherpa VAD engine loaded model from {ModelPath}",
                    onnxPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load VAD model from {ModelPath}", onnxPath);
                IsReady = false;
            }
        }
    }

    private static string? ResolveModelFile(string modelPath)
    {
        if (string.IsNullOrWhiteSpace(modelPath))
        {
            return null;
        }

        if (File.Exists(modelPath))
        {
            return modelPath;
        }

        if (Directory.Exists(modelPath))
        {
            return Directory.EnumerateFiles(modelPath, "*.onnx", SearchOption.AllDirectories).FirstOrDefault();
        }

        return null;
    }

    private static VadModelConfig BuildConfig(string modelPath, VadOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);

        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException(
                $"VAD model file '{modelPath}' was not found.",
                modelPath);
        }

        return new VadModelConfig
        {
            SileroVad = new SileroVadModelConfig
            {
                Model = modelPath,
                Threshold = options.Threshold,
                MinSpeechDuration = options.MinSpeechDuration,
                MinSilenceDuration = options.MinSilenceDuration,
            },
            SampleRate = options.SampleRate,
        };
    }
}
