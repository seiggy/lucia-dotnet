using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SherpaOnnx;
using lucia.Wyoming.Models;

namespace lucia.Wyoming.WakeWord;

public sealed class SherpaWakeWordDetector : IWakeWordDetector, IDisposable
{
    private readonly object _lock = new();
    private readonly List<KeywordSpotter> _retiredSpotters = [];
    private readonly WakeWordOptions _options;
    private readonly ILogger<SherpaWakeWordDetector> _logger;
    private readonly IWakeWordChangeNotifier? _changeNotifier;
    private readonly IModelChangeNotifier _modelChangeNotifier;
    private KeywordSpotter? _spotter;
    private string _modelPath;

    public bool IsReady { get; private set; }

    public SherpaWakeWordDetector(
        IOptions<WakeWordOptions> options,
        IModelChangeNotifier modelChangeNotifier,
        IWakeWordChangeNotifier? changeNotifier,
        ILogger<SherpaWakeWordDetector> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(modelChangeNotifier);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options.Value;
        _logger = logger;
        _changeNotifier = changeNotifier;
        _modelChangeNotifier = modelChangeNotifier;
        _modelPath = _options.ModelPath ?? string.Empty;

        TryLoadModel(_modelPath);

        if (_changeNotifier is not null)
        {
            _changeNotifier.KeywordsChanged += OnKeywordsChanged;
        }

        _modelChangeNotifier.ActiveModelChanged += OnActiveModelChanged;
    }

    public IWakeWordSession CreateSession()
    {
        KeywordSpotter spotter;
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_spotter is null, this);

            if (!IsReady)
            {
                throw new InvalidOperationException("Wake word detector is not ready");
            }

            spotter = _spotter;
        }

        var stream = spotter.CreateStream();
        return new SherpaWakeWordSession(spotter, stream);
    }

    public void Dispose()
    {
        _modelChangeNotifier.ActiveModelChanged -= OnActiveModelChanged;

        if (_changeNotifier is not null)
        {
            _changeNotifier.KeywordsChanged -= OnKeywordsChanged;
        }

        lock (_lock)
        {
            IsReady = false;
            _spotter?.Dispose();
            _spotter = null;

            foreach (var retiredSpotter in _retiredSpotters)
            {
                retiredSpotter.Dispose();
            }

            _retiredSpotters.Clear();
        }
    }

    private void OnActiveModelChanged(ActiveModelChangedEvent evt)
    {
        if (evt.EngineType != EngineType.WakeWord) return;

        _logger.LogInformation("Reloading wake word detector with model {ModelId}", evt.ModelId);
        TryLoadModel(evt.ModelPath);
    }

    private void TryLoadModel(string modelPath)
    {
        if (string.IsNullOrWhiteSpace(modelPath))
        {
            _logger.LogWarning(
                "Wake word model path not configured. Wake word detection is disabled until a model is installed.");
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
                var config = BuildConfig(modelPath, _options);
                var newSpotter = new KeywordSpotter(config);
                var oldSpotter = _spotter;

                _spotter = newSpotter;
                _modelPath = modelPath;
                IsReady = true;

                if (oldSpotter is not null)
                {
                    _retiredSpotters.Add(oldSpotter);
                }

                _logger.LogInformation(
                    "Sherpa wake word detector loaded model from {ModelPath}", modelPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load wake word model from {ModelPath}", modelPath);
                IsReady = false;
            }
        }
    }

    private void OnKeywordsChanged()
    {
        if (string.IsNullOrWhiteSpace(_modelPath))
        {
            _logger.LogDebug("Keywords changed but model path not configured, skipping rebuild");
            return;
        }

        _logger.LogInformation("Keywords changed, rebuilding keyword spotter");

        lock (_lock)
        {
            try
            {
                var config = BuildConfig(_modelPath, _options);
                var newSpotter = new KeywordSpotter(config);
                var oldSpotter = _spotter;

                _spotter = newSpotter;
                IsReady = true;

                if (oldSpotter is not null)
                {
                    _retiredSpotters.Add(oldSpotter);
                }

                _logger.LogInformation("Keyword spotter rebuilt successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to rebuild keyword spotter after keyword change");
            }
        }
    }

    private static KeywordSpotterConfig BuildConfig(string modelPath, WakeWordOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);

        var modelDirectory = Path.GetFullPath(modelPath);
        if (!Directory.Exists(modelDirectory))
        {
            throw new DirectoryNotFoundException($"Wake word model directory was not found: {modelDirectory}");
        }

        var config = new KeywordSpotterConfig();
        config.FeatConfig.SampleRate = 16000;
        config.FeatConfig.FeatureDim = 80;
        config.MaxActivePaths = Math.Max(1, options.MaxConcurrentKeywords);
        config.NumTrailingBlanks = 1;
        config.KeywordsScore = options.Sensitivity;

        config.ModelConfig.Transducer.Encoder = GetRequiredModelFile(modelDirectory, "*encoder*.onnx");
        config.ModelConfig.Transducer.Decoder = GetRequiredModelFile(modelDirectory, "*decoder*.onnx");
        config.ModelConfig.Transducer.Joiner = GetRequiredModelFile(modelDirectory, "*joiner*.onnx");
        config.ModelConfig.Tokens = GetRequiredFile(modelDirectory, "tokens.txt");

        var keywordsFile = ResolveKeywordsFile(modelDirectory, options.KeywordsFile);
        config.KeywordsFile = keywordsFile;

        return config;
    }

    private static string ResolveKeywordsFile(string modelDirectory, string configuredKeywordsFile)
    {
        if (!string.IsNullOrWhiteSpace(configuredKeywordsFile))
        {
            var resolvedPath = Path.IsPathRooted(configuredKeywordsFile)
                ? configuredKeywordsFile
                : Path.Combine(modelDirectory, configuredKeywordsFile);

            if (File.Exists(resolvedPath))
            {
                return resolvedPath;
            }

            throw new FileNotFoundException($"Configured wake word keywords file was not found: {resolvedPath}");
        }

        return GetRequiredFile(modelDirectory, "keywords.txt");
    }

    private static string GetRequiredModelFile(string modelDirectory, string pattern)
    {
        var file = Directory
            .GetFiles(modelDirectory, pattern, SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        return file ?? throw new FileNotFoundException(
            $"Wake word model file matching '{pattern}' was not found in {modelDirectory}.");
    }

    private static string GetRequiredFile(string modelDirectory, string fileName)
    {
        var filePath = Path.Combine(modelDirectory, fileName);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Required wake word file was not found: {filePath}");
        }

        return filePath;
    }
}
