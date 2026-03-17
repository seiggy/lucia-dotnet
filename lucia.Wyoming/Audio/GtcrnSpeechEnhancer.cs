using lucia.Wyoming.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;

namespace lucia.Wyoming.Audio;

/// <summary>
/// Speech enhancement engine using streaming GTCRN via ONNX Runtime.
/// Creates per-stream sessions with isolated cache state.
/// </summary>
public sealed class GtcrnSpeechEnhancer : ISpeechEnhancer, IDisposable
{
    private readonly IModelChangeNotifier _modelChangeNotifier;
    private readonly OnnxProviderDetector _providerDetector;
    private readonly SpeechEnhancementOptions _options;
    private readonly ILogger<GtcrnSpeechEnhancer> _logger;
    private readonly object _lock = new();
    private InferenceSession? _onnxSession;

    public bool IsReady => _onnxSession is not null;

    public GtcrnSpeechEnhancer(
        IOptions<SpeechEnhancementOptions> options,
        IModelChangeNotifier modelChangeNotifier,
        OnnxProviderDetector providerDetector,
        ILogger<GtcrnSpeechEnhancer> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(modelChangeNotifier);
        ArgumentNullException.ThrowIfNull(providerDetector);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options.Value;
        _modelChangeNotifier = modelChangeNotifier;
        _providerDetector = providerDetector;
        _logger = logger;

        if (_options.Enabled)
        {
            TryLoadModel(_options.ModelBasePath);
        }

        _modelChangeNotifier.ActiveModelChanged += OnActiveModelChanged;
    }

    public ISpeechEnhancerSession CreateSession()
    {
        lock (_lock)
        {
            if (_onnxSession is null)
            {
                throw new InvalidOperationException("Speech enhancement engine is not ready.");
            }

            return new GtcrnStreamingSession(_onnxSession);
        }
    }

    public void Dispose()
    {
        _modelChangeNotifier.ActiveModelChanged -= OnActiveModelChanged;
        lock (_lock)
        {
            _onnxSession?.Dispose();
            _onnxSession = null;
        }
    }

    private void OnActiveModelChanged(ActiveModelChangedEvent evt)
    {
        if (evt.EngineType != EngineType.SpeechEnhancement) return;
        _logger.LogInformation("Reloading speech enhancement engine with model {ModelId}", evt.ModelId);
        TryLoadModel(evt.ModelPath);
    }

    private void TryLoadModel(string modelPath)
    {
        var onnxFile = ResolveOnnxFile(modelPath);
        if (string.IsNullOrWhiteSpace(onnxFile))
        {
            _logger.LogInformation("Speech enhancement model not available at {Path}", modelPath ?? "(not configured)");
            return;
        }

        lock (_lock)
        {
            try
            {
                var sessionOptions = new SessionOptions();
                sessionOptions.InterOpNumThreads = 1;
                sessionOptions.IntraOpNumThreads = 2;
                sessionOptions.LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR;
                _providerDetector.ConfigureSessionOptions(sessionOptions, _logger);

                var oldSession = _onnxSession;
                _onnxSession = new InferenceSession(onnxFile, sessionOptions);
                oldSession?.Dispose();

                _logger.LogInformation(
                    "Speech enhancement engine loaded streaming GTCRN model from {Path}",
                    onnxFile);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load speech enhancement model from {Path}", onnxFile);
            }
        }
    }

    private static string? ResolveOnnxFile(string? modelPath)
    {
        if (string.IsNullOrWhiteSpace(modelPath)) return null;
        if (File.Exists(modelPath) && modelPath.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase))
            return modelPath;
        if (Directory.Exists(modelPath))
            return Directory.EnumerateFiles(modelPath, "*.onnx", SearchOption.AllDirectories).FirstOrDefault();
        return null;
    }
}
