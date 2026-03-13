using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucia.Wyoming.WakeWord;

/// <summary>
/// Manages custom wake words: registration, calibration, and keyword file generation.
/// Uses open-vocabulary keyword spotting — no audio training required for basic functionality.
/// Optional calibration (3-5 samples) auto-tunes boost/threshold.
/// </summary>
public sealed class CustomWakeWordManager : IWakeWordChangeNotifier
{
    private readonly IWakeWordStore _store;
    private readonly WakeWordTokenizer _tokenizer;
    private readonly WakeWordOptions _options;
    private readonly ILogger<CustomWakeWordManager> _logger;
    private readonly string _keywordsFilePath;

    public event Action? KeywordsChanged;

    public CustomWakeWordManager(
        IWakeWordStore store,
        WakeWordTokenizer tokenizer,
        IOptions<WakeWordOptions> options,
        ILogger<CustomWakeWordManager> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(tokenizer);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _store = store;
        _tokenizer = tokenizer;
        _options = options.Value;
        _logger = logger;
        _keywordsFilePath = ResolveKeywordsFilePath(_options);

        var tokensPath = Path.Combine(_options.ModelPath, "tokens.txt");
        if (File.Exists(tokensPath))
        {
            _tokenizer.LoadVocabulary(tokensPath);
            _logger.LogInformation("Wake word tokenizer loaded vocabulary from {Path}", tokensPath);
        }
        else
        {
            _logger.LogWarning(
                "Wake word tokens.txt not found at {Path}. Custom wake word registration will not be available until a KWS model is installed.",
                tokensPath);
        }
    }

    /// <summary>
    /// Register a new custom wake word from plain text.
    /// Zero audio recordings required.
    /// </summary>
    public async Task<CustomWakeWord> RegisterWakeWordAsync(
        string phrase,
        string? userId = null,
        CancellationToken ct = default)
    {
        ValidatePhrase(phrase);

        if (!_tokenizer.IsLoaded)
        {
            throw new InvalidOperationException(
                "Wake word tokenizer is not initialized. Ensure a KWS model with tokens.txt is installed.");
        }

        var now = DateTimeOffset.UtcNow;
        var wakeWord = new CustomWakeWord
        {
            Id = Guid.NewGuid().ToString("N"),
            Phrase = phrase,
            Tokens = _tokenizer.Tokenize(phrase),
            UserId = userId,
            BoostScore = 1.5f,
            Threshold = 0.30f,
            IsCalibrated = false,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await _store.SaveAsync(wakeWord, ct).ConfigureAwait(false);
        await ReloadKeywordsAsync(ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Registered custom wake word '{Phrase}' (id: {Id})",
            phrase,
            wakeWord.Id);

        return wakeWord;
    }

    /// <summary>
    /// Calibrate a wake word using recorded audio samples.
    /// Adjusts boost score and threshold for optimal detection.
    /// </summary>
    public async Task<CalibrationResult> CalibrateAsync(
        string wakeWordId,
        IReadOnlyList<CalibrationDetection> detections,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wakeWordId);
        ArgumentNullException.ThrowIfNull(detections);

        if (detections.Count == 0)
        {
            throw new ArgumentException("At least one calibration detection is required.", nameof(detections));
        }

        var wakeWord = await _store.GetAsync(wakeWordId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Wake word '{wakeWordId}' not found");

        var (boost, threshold) = AutoTune(detections, wakeWord);
        var detectionRate = detections.Count(d => d.Detected) / (float)detections.Count;
        var avgConfidence = detections
            .Where(d => d.Detected)
            .Select(d => d.Confidence)
            .DefaultIfEmpty(0)
            .Average();

        var updated = wakeWord with
        {
            BoostScore = boost,
            Threshold = threshold,
            IsCalibrated = true,
            CalibratedAt = DateTimeOffset.UtcNow,
            CalibrationSamples = detections.Count,
            DetectionRate = detectionRate,
            AverageConfidence = avgConfidence,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        await _store.SaveAsync(updated, ct).ConfigureAwait(false);
        await ReloadKeywordsAsync(ct).ConfigureAwait(false);

        var recommendation = GenerateRecommendation(detectionRate, avgConfidence);

        _logger.LogInformation(
            "Calibrated wake word '{Phrase}': boost={Boost:F2}, threshold={Threshold:F2}, detection={Rate:P0}",
            wakeWord.Phrase,
            boost,
            threshold,
            detectionRate);

        return new CalibrationResult
        {
            DetectionRate = detectionRate,
            AverageConfidence = avgConfidence,
            BoostScore = boost,
            Threshold = threshold,
            Recommendation = recommendation,
        };
    }

    /// <summary>
    /// Regenerate keywords.txt from all stored wake words.
    /// </summary>
    public async Task ReloadKeywordsAsync(CancellationToken ct)
    {
        var allWords = await _store.GetAllAsync(ct).ConfigureAwait(false);
        var lines = allWords.Select(w => $"{w.Tokens} :{w.BoostScore:F2} #{w.Threshold:F2}");
        var content = string.Join("\n", lines);

        var directory = Path.GetDirectoryName(_keywordsFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(_keywordsFilePath, content, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Reloaded {Count} wake words into keywords file {KeywordsFilePath}",
            allWords.Count,
            _keywordsFilePath);

        KeywordsChanged?.Invoke();
    }

    private static string ResolveKeywordsFilePath(WakeWordOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.ModelPath))
        {
            throw new InvalidOperationException(
                $"{WakeWordOptions.SectionName}:{nameof(WakeWordOptions.ModelPath)} must be configured.");
        }

        var modelDirectory = Path.GetFullPath(options.ModelPath);
        return string.IsNullOrWhiteSpace(options.KeywordsFile)
            ? Path.Combine(modelDirectory, "keywords.txt")
            : Path.IsPathRooted(options.KeywordsFile)
                ? options.KeywordsFile
                : Path.Combine(modelDirectory, options.KeywordsFile);
    }

    private static void ValidatePhrase(string phrase)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phrase);

        if (phrase.Length < 3)
        {
            throw new ArgumentException(
                "Wake word phrase is too short (minimum 3 characters)",
                nameof(phrase));
        }

        if (phrase.Length > 100)
        {
            throw new ArgumentException(
                "Wake word phrase is too long (maximum 100 characters)",
                nameof(phrase));
        }
    }

    private static (float boost, float threshold) AutoTune(
        IReadOnlyList<CalibrationDetection> detections,
        CustomWakeWord current)
    {
        var detectionRate = detections.Count(d => d.Detected) / (float)detections.Count;
        var avgConfidence = detections
            .Where(d => d.Detected)
            .Select(d => d.Confidence)
            .DefaultIfEmpty(0)
            .Average();

        var boost = current.BoostScore;
        var threshold = current.Threshold;

        if (detectionRate >= 1.0f && avgConfidence > 0.6f)
        {
            boost = Math.Max(0.8f, boost - 0.3f);
            threshold = Math.Min(0.5f, threshold + 0.05f);
        }
        else if (detectionRate < 0.8f)
        {
            boost = Math.Min(3.0f, boost + 0.5f);
            threshold = Math.Max(0.15f, threshold - 0.05f);
        }

        return (boost, threshold);
    }

    private static string GenerateRecommendation(float detectionRate, float avgConfidence)
        => (detectionRate, avgConfidence) switch
        {
            (>= 1.0f, > 0.7f) => "Excellent — detection sensitivity set to high",
            (>= 0.8f, > 0.5f) => "Good — detection should work well in most environments",
            (>= 0.6f, _) => "Fair — try speaking more clearly or reducing background noise",
            _ => "Low detection rate — consider choosing a more distinct wake phrase",
        };
}
