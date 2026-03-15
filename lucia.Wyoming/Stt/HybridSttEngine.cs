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
    private readonly object _lock = new();
    private readonly List<OfflineRecognizer> _retiredRecognizers = [];
    private OfflineRecognizer? _recognizer;

    public bool IsReady { get; private set; }

    public HybridSttEngine(
        IOptions<HybridSttOptions> options,
        IModelChangeNotifier modelChangeNotifier,
        ILogger<HybridSttEngine> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(modelChangeNotifier);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options.Value;
        _modelChangeNotifier = modelChangeNotifier;
        _logger = logger;

        if (_options.Enabled)
            TryLoadModel(_options.ModelPath);

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
                _options.MinAudioMs, _options.MaxContextSeconds, _logger);
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

    private static string? FindFirst(string root, string fileName) =>
        Directory.EnumerateFiles(root, fileName, SearchOption.AllDirectories)
            .OrderBy(static p => p, StringComparer.Ordinal)
            .FirstOrDefault();

    private static string? FindFirstMatch(string root, string pattern) =>
        Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories)
            .OrderBy(static p => p, StringComparer.Ordinal)
            .FirstOrDefault();
}
