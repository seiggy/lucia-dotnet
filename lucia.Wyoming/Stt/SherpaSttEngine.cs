using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SherpaOnnx;

namespace lucia.Wyoming.Stt;

public sealed class SherpaSttEngine : ISttEngine
{
    private readonly ILogger<SherpaSttEngine> _logger;
    private readonly OnlineRecognizer? _recognizer;

    public bool IsReady { get; private set; }

    public SherpaSttEngine(
        IOptions<SttOptions> options,
        ILogger<SherpaSttEngine> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;

        try
        {
            var config = BuildConfig(options.Value);
            _recognizer = new OnlineRecognizer(config);
            IsReady = true;

            _logger.LogInformation(
                "Sherpa STT engine initialized with model path {ModelPath}",
                options.Value.ModelPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Sherpa STT engine");
            IsReady = false;
        }
    }

    public ISttSession CreateSession()
    {
        ObjectDisposedException.ThrowIf(!IsReady || _recognizer is null, this);

        var stream = _recognizer.CreateStream();
        return new SherpaSttSession(_recognizer, stream);
    }

    public void Dispose()
    {
        IsReady = false;
        _recognizer?.Dispose();
    }

    private static OnlineRecognizerConfig BuildConfig(SttOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.ModelPath))
        {
            throw new InvalidOperationException("STT model path must be configured.");
        }

        if (!Directory.Exists(options.ModelPath))
        {
            throw new DirectoryNotFoundException(
                $"STT model directory '{options.ModelPath}' was not found.");
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

        var tokensFile = FindFirst(options.ModelPath, "tokens.txt");
        if (tokensFile is null)
        {
            throw new FileNotFoundException(
                $"No tokens.txt file was found under '{options.ModelPath}'.");
        }

        config.ModelConfig.Tokens = tokensFile;

        var encoderFile = FindFirstMatch(options.ModelPath, "*encoder*.onnx");
        var decoderFile = FindFirstMatch(options.ModelPath, "*decoder*.onnx");
        var joinerFile = FindFirstMatch(options.ModelPath, "*joiner*.onnx");

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

        var ctcModelFile = FindFirstMatch(options.ModelPath, "*ctc*.onnx")
            ?? FindFirstMatch(options.ModelPath, "*model*.onnx")
            ?? FindSingleOnnxFile(options.ModelPath);

        if (ctcModelFile is not null)
        {
            config.ModelConfig.Zipformer2Ctc.Model = ctcModelFile;
            return config;
        }

        throw new InvalidOperationException(
            $"Could not detect a supported sherpa-onnx streaming model under '{options.ModelPath}'.");
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
