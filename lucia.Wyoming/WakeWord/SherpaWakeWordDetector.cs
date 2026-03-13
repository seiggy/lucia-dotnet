using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SherpaOnnx;

namespace lucia.Wyoming.WakeWord;

public sealed class SherpaWakeWordDetector : IWakeWordDetector
{
    private KeywordSpotter? _spotter;
    private readonly WakeWordOptions _options;
    private readonly ILogger<SherpaWakeWordDetector> _logger;

    public bool IsReady { get; private set; }

    public SherpaWakeWordDetector(
        IOptions<WakeWordOptions> options,
        ILogger<SherpaWakeWordDetector> logger)
    {
        _options = options.Value;
        _logger = logger;

        try
        {
            var config = BuildConfig(_options);
            _spotter = new KeywordSpotter(config);
            IsReady = true;
            _logger.LogInformation("Sherpa wake word detector initialized");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Sherpa wake word detector");
            IsReady = false;
        }
    }

    public IWakeWordSession CreateSession()
    {
        ObjectDisposedException.ThrowIf(_spotter is null, this);

        if (!IsReady)
        {
            throw new InvalidOperationException("Wake word detector is not ready");
        }

        var stream = _spotter.CreateStream();
        return new SherpaWakeWordSession(_spotter, stream);
    }

    public void Dispose()
    {
        IsReady = false;
        _spotter?.Dispose();
        _spotter = null;
    }

    private static KeywordSpotterConfig BuildConfig(WakeWordOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.ModelPath))
        {
            throw new InvalidOperationException(
                $"{WakeWordOptions.SectionName}:{nameof(WakeWordOptions.ModelPath)} must be configured.");
        }

        var modelDirectory = Path.GetFullPath(options.ModelPath);
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
