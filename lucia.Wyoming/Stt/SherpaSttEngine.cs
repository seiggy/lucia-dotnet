using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SherpaOnnx;
using lucia.Wyoming.Models;

namespace lucia.Wyoming.Stt;

public sealed class SherpaSttEngine : ISttEngine
{
    private readonly ILogger<SherpaSttEngine> _logger;
    private readonly IModelChangeNotifier _modelChangeNotifier;
    private readonly object _lock = new();
    private readonly List<OnlineRecognizer> _retiredRecognizers = [];
    private readonly SttOptions _options;
    private readonly int _sampleRate;
    private OnlineRecognizer? _recognizer;

    public bool IsReady { get; private set; }

    public SherpaSttEngine(
        IOptions<SttOptions> options,
        IModelChangeNotifier modelChangeNotifier,
        ILogger<SherpaSttEngine> logger)
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

    public ISttSession CreateSession()
    {
        OnlineRecognizer recognizer;
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(!IsReady || _recognizer is null, this);
            recognizer = _recognizer;
        }

        var stream = recognizer.CreateStream();
        return new SherpaSttSession(recognizer, stream, _sampleRate);
    }

    public void Dispose()
    {
        _modelChangeNotifier.ActiveModelChanged -= OnActiveModelChanged;

        lock (_lock)
        {
            IsReady = false;
            _recognizer?.Dispose();
            _recognizer = null;

            foreach (var retiredRecognizer in _retiredRecognizers)
            {
                retiredRecognizer.Dispose();
            }

            _retiredRecognizers.Clear();
        }
    }

    private void OnActiveModelChanged(ActiveModelChangedEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);

        _logger.LogInformation("Reloading STT engine with model {ModelId}", evt.ModelId);
        TryLoadModel(evt.ModelPath);
    }

    private void TryLoadModel(string modelPath)
    {
        if (string.IsNullOrWhiteSpace(modelPath) || !Directory.Exists(modelPath))
        {
            _logger.LogWarning("STT model path not configured or missing: {ModelPath}", modelPath);
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
                var newRecognizer = new OnlineRecognizer(config);
                var oldRecognizer = _recognizer;

                _recognizer = newRecognizer;
                IsReady = true;

                if (oldRecognizer is not null)
                {
                    _retiredRecognizers.Add(oldRecognizer);
                }

                _logger.LogInformation(
                    "Sherpa STT engine loaded model from {ModelPath}",
                    modelPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load STT model from {ModelPath}", modelPath);
                IsReady = false;
            }
        }
    }

    private static OnlineRecognizerConfig BuildConfig(string modelPath, SttOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);

        if (!Directory.Exists(modelPath))
        {
            throw new DirectoryNotFoundException(
                $"STT model directory '{modelPath}' was not found.");
        }

        var config = new OnlineRecognizerConfig
        {
            DecodingMethod = "greedy_search",
            EnableEndpoint = 1,
            Rule1MinTrailingSilence = 2.4f,
            Rule2MinTrailingSilence = 1.2f,
            Rule3MinUtteranceLength = 20.0f,
        };

        config.FeatConfig.SampleRate = options.SampleRate;
        config.FeatConfig.FeatureDim = 80;
        config.ModelConfig.NumThreads = options.NumThreads;
        config.ModelConfig.Provider = string.IsNullOrWhiteSpace(options.Provider)
            ? "cpu"
            : options.Provider;

        var tokensFile = FindFirst(modelPath, "tokens.txt");
        if (tokensFile is null)
        {
            throw new FileNotFoundException(
                $"No tokens.txt file was found under '{modelPath}'.");
        }

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

        if (encoderFile is not null && decoderFile is not null)
        {
            config.ModelConfig.Paraformer.Encoder = encoderFile;
            config.ModelConfig.Paraformer.Decoder = decoderFile;
            return config;
        }

        var ctcModelFile = FindFirstMatch(modelPath, "*ctc*.onnx")
            ?? FindFirstMatch(modelPath, "*model*.onnx")
            ?? FindSingleOnnxFile(modelPath);

        if (ctcModelFile is not null)
        {
            config.ModelConfig.Zipformer2Ctc.Model = ctcModelFile;
            return config;
        }

        throw new InvalidOperationException(
            $"Could not detect a supported sherpa-onnx streaming model under '{modelPath}'.");
    }

    private static string? FindFirst(string root, string fileName) =>
        Directory
            .EnumerateFiles(root, fileName, SearchOption.AllDirectories)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .FirstOrDefault();

    private static string? FindFirstMatch(string root, string pattern) =>
        Directory
            .EnumerateFiles(root, pattern, SearchOption.AllDirectories)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .FirstOrDefault();

    private static string? FindSingleOnnxFile(string root)
    {
        var modelFiles = Directory
            .EnumerateFiles(root, "*.onnx", SearchOption.AllDirectories)
            .Where(static path =>
                !path.Contains("encoder", StringComparison.OrdinalIgnoreCase)
                && !path.Contains("decoder", StringComparison.OrdinalIgnoreCase)
                && !path.Contains("joiner", StringComparison.OrdinalIgnoreCase))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();

        return modelFiles.Length == 1 ? modelFiles[0] : null;
    }
}
