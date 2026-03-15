using lucia.Wyoming.Audio;
using lucia.Wyoming.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SherpaOnnx;

namespace lucia.Wyoming.Stt;

/// <summary>
/// Hybrid streaming STT engine: uses an offline model (Parakeet TDT) with periodic
/// re-transcription of a growing audio buffer to produce progressive partial results.
///
/// Every <see cref="HybridSttOptions.RefreshIntervalMs"/> of new audio triggers a
/// re-transcription of the full utterance buffer, giving real-time transcript refinement
/// with offline-model accuracy.
/// </summary>
public sealed class HybridSttEngine : ISttEngine, IDisposable
{
    private readonly ILogger<HybridSttEngine> _logger;
    private readonly IModelChangeNotifier _modelChangeNotifier;
    private readonly HybridSttOptions _options;
    private readonly SttModelOptions _sttModelOptions;
    private readonly object _lock = new();
    private readonly List<OfflineRecognizer> _retiredRecognizers = [];
    private OfflineRecognizer? _recognizer;

    public bool IsReady { get; private set; }

    public HybridSttEngine(
        IOptions<HybridSttOptions> options,
        IOptions<SttModelOptions> sttModelOptions,
        IModelChangeNotifier modelChangeNotifier,
        ILogger<HybridSttEngine> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(sttModelOptions);
        ArgumentNullException.ThrowIfNull(modelChangeNotifier);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options.Value;
        _sttModelOptions = sttModelOptions.Value;
        _modelChangeNotifier = modelChangeNotifier;
        _logger = logger;

        if (_options.Enabled)
        {
            var modelPath = _options.ModelPath;

            // Auto-discover: if no explicit path, scan the STT model base path
            if (string.IsNullOrWhiteSpace(modelPath) || !Directory.Exists(modelPath))
                modelPath = AutoDiscoverOfflineModel();

            if (modelPath is not null)
                TryLoadModel(modelPath);
        }

        _modelChangeNotifier.ActiveModelChanged += OnActiveModelChanged;
    }

    public ISttSession CreateSession()
    {
        lock (_lock)
        {
            if (_recognizer is null)
                throw new InvalidOperationException("Hybrid STT engine is not ready.");

            return new HybridSttSession(
                _recognizer, _options.SampleRate, _options.RefreshIntervalMs,
                _options.MinAudioMs, _options.MaxContextSeconds,
                _options.ProgressiveThresholdSeconds, _logger);
        }
    }

    public void Dispose()
    {
        _modelChangeNotifier.ActiveModelChanged -= OnActiveModelChanged;
        lock (_lock)
        {
            IsReady = false;
            _recognizer?.Dispose();
            _recognizer = null;

            foreach (var r in _retiredRecognizers)
                r.Dispose();
            _retiredRecognizers.Clear();
        }
    }

    private void TryLoadModel(string modelPath)
    {
        if (string.IsNullOrWhiteSpace(modelPath) || !Directory.Exists(modelPath))
        {
            _logger.LogInformation("Hybrid STT model path not configured or missing: {Path}",
                modelPath ?? "(not configured)");
            return;
        }

        lock (_lock)
        {
            try
            {
                var config = BuildConfig(modelPath);
                var newRecognizer = new OfflineRecognizer(config);
                var oldRecognizer = _recognizer;

                _recognizer = newRecognizer;
                IsReady = true;

                if (oldRecognizer is not null)
                    _retiredRecognizers.Add(oldRecognizer);

                _logger.LogInformation("Hybrid STT engine loaded model from {ModelPath}", modelPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load hybrid STT model from {Path}", modelPath);
                IsReady = false;
            }
        }
    }

    private OfflineRecognizerConfig BuildConfig(string modelPath)
    {
        var config = new OfflineRecognizerConfig();
        config.FeatConfig.SampleRate = _options.SampleRate;
        config.FeatConfig.FeatureDim = 80;
        config.ModelConfig.NumThreads = _options.NumThreads;
        config.ModelConfig.Provider = string.IsNullOrWhiteSpace(_options.Provider)
            ? "cpu" : _options.Provider;

        var tokensFile = FindFirst(modelPath, "tokens.txt");
        if (tokensFile is null)
            throw new FileNotFoundException($"No tokens.txt found under '{modelPath}'.");
        config.ModelConfig.Tokens = tokensFile;

        var encoderFile = FindFirstMatch(modelPath, "*encoder*.onnx");
        var decoderFile = FindFirstMatch(modelPath, "*decoder*.onnx");
        var joinerFile = FindFirstMatch(modelPath, "*joiner*.onnx");

        if (encoderFile is not null && decoderFile is not null && joinerFile is not null)
        {
            config.ModelConfig.Transducer.Encoder = encoderFile;
            config.ModelConfig.Transducer.Decoder = decoderFile;
            config.ModelConfig.Transducer.Joiner = joinerFile;
            return config;
        }

        var ctcModel = FindFirstMatch(modelPath, "model*.onnx");
        if (ctcModel is not null)
        {
            config.ModelConfig.NeMoCtc.Model = ctcModel;
            return config;
        }

        throw new InvalidOperationException(
            $"Could not detect a supported offline model under '{modelPath}'.");
    }

    private void OnActiveModelChanged(ActiveModelChangedEvent evt)
    {
        if (evt.EngineType != EngineType.OfflineStt) return;
        _logger.LogInformation("Reloading hybrid STT engine with model {ModelId}", evt.ModelId);
        TryLoadModel(evt.ModelPath);
    }

    /// <summary>
    /// Scans the STT model base path for the best available offline model.
    /// Prefers larger Parakeet TDT models, then any model with tokens.txt that isn't streaming-only.
    /// </summary>
    private string? AutoDiscoverOfflineModel()
    {
        var basePath = _sttModelOptions.ModelBasePath;
        if (string.IsNullOrWhiteSpace(basePath) || !Directory.Exists(basePath))
        {
            _logger.LogDebug("STT model base path not configured for auto-discovery");
            return null;
        }

        var candidates = Directory.EnumerateDirectories(basePath)
            .Where(d => Directory.EnumerateFiles(d, "tokens.txt", SearchOption.AllDirectories).Any())
            .Where(d =>
            {
                var name = Path.GetFileName(d) ?? "";
                // Offline-capable: parakeet, nemo non-streaming, whisper, sense-voice
                // Exclude streaming-only models (zipformer streaming, conformer streaming)
                return name.Contains("parakeet", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("whisper", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("sense-voice", StringComparison.OrdinalIgnoreCase)
                    || (name.Contains("nemo", StringComparison.OrdinalIgnoreCase)
                        && !name.Contains("streaming", StringComparison.OrdinalIgnoreCase));
            })
            .ToList();

        if (candidates.Count == 0)
        {
            _logger.LogInformation("No offline STT models found in {BasePath}", basePath);
            return null;
        }

        // Prefer larger models (0.6b > 110m)
        var best = candidates
            .OrderByDescending(d =>
            {
                var name = Path.GetFileName(d) ?? "";
                if (name.Contains("1.1b", StringComparison.OrdinalIgnoreCase)) return 5;
                if (name.Contains("0.6b", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("0-6b", StringComparison.OrdinalIgnoreCase)) return 4;
                if (name.Contains("parakeet", StringComparison.OrdinalIgnoreCase)) return 3;
                if (name.Contains("whisper", StringComparison.OrdinalIgnoreCase)) return 2;
                return 1;
            })
            .First();

        _logger.LogInformation("Auto-discovered offline STT model: {Model}", Path.GetFileName(best));
        return best;
    }

    private static string? FindFirst(string root, string fileName) =>
        Directory.EnumerateFiles(root, fileName, SearchOption.AllDirectories)
            .OrderBy(static p => p, StringComparer.Ordinal)
            .FirstOrDefault();

    private static string? FindFirstMatch(string root, string pattern) =>
        Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories)
            .OrderBy(static p => p, StringComparer.Ordinal)
            .FirstOrDefault();
}
