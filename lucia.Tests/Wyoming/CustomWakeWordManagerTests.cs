using lucia.Wyoming.WakeWord;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace lucia.Tests.Wyoming;

public sealed class CustomWakeWordManagerTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "lucia-tests", Guid.NewGuid().ToString("N"));
    private readonly string _modelPath;
    private readonly string _tokensFilePath;
    private readonly string _keywordsFilePath;

    public CustomWakeWordManagerTests()
    {
        _modelPath = Path.Combine(_tempRoot, "model");
        _tokensFilePath = Path.Combine(_modelPath, "tokens.txt");
        _keywordsFilePath = Path.Combine(_modelPath, "keywords.txt");

        Directory.CreateDirectory(_modelPath);
        File.WriteAllText(
            _tokensFilePath,
            string.Join(
                Environment.NewLine,
                [
                    "▁HEY 0",
                    "▁JARVIS 1",
                    "▁WAKE 2",
                    "▁WORD 3",
                    "▁LUCIA 4",
                ]));
    }

    [Fact]
    public async Task RegisterWakeWord_CreatesEntry()
    {
        var (manager, store) = CreateManager();

        var wakeWord = await manager.RegisterWakeWordAsync("Hey Jarvis");
        var stored = await store.GetAsync(wakeWord.Id, CancellationToken.None);

        Assert.NotNull(stored);
        Assert.Equal("Hey Jarvis", stored.Phrase);
        Assert.False(string.IsNullOrWhiteSpace(stored.Tokens));
        Assert.NotEmpty(stored.Tokens.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public async Task RegisterWakeWord_EmptyPhrase_Throws()
    {
        var (manager, _) = CreateManager();

        foreach (var phrase in new string?[] { null, string.Empty, "   " })
        {
            var exception = await Record.ExceptionAsync(() => manager.RegisterWakeWordAsync(phrase!));

            Assert.NotNull(exception);
            var argumentException = Assert.IsAssignableFrom<ArgumentException>(exception);
            Assert.Equal("phrase", argumentException.ParamName);
        }
    }

    [Fact]
    public async Task RegisterWakeWord_TooShort_Throws()
    {
        var (manager, _) = CreateManager();

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => manager.RegisterWakeWordAsync("Hi"));

        Assert.Equal("phrase", exception.ParamName);
        Assert.Contains("minimum 3 characters", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Calibrate_AllDetected_TightensThreshold()
    {
        var (manager, store) = CreateManager();
        var wakeWord = await manager.RegisterWakeWordAsync("Hey Jarvis");
        var detections = Enumerable.Range(0, 5)
            .Select(_ => Detection(detected: true, confidence: 0.65f))
            .ToArray();

        var result = await manager.CalibrateAsync(wakeWord.Id, detections, CancellationToken.None);
        var stored = await store.GetAsync(wakeWord.Id, CancellationToken.None);

        Assert.NotNull(stored);
        Assert.True(result.BoostScore < wakeWord.BoostScore);
        Assert.True(result.Threshold > wakeWord.Threshold);
        Assert.Equal(result.BoostScore, stored.BoostScore);
        Assert.Equal(result.Threshold, stored.Threshold);
    }

    [Fact]
    public async Task Calibrate_LowDetectionRate_LoosensThreshold()
    {
        var (manager, store) = CreateManager();
        var wakeWord = await manager.RegisterWakeWordAsync("Hey Jarvis");
        var detections = new[]
        {
            Detection(detected: true, confidence: 0.55f),
            Detection(detected: true, confidence: 0.52f),
            Detection(detected: false, confidence: 0.20f),
            Detection(detected: false, confidence: 0.18f),
            Detection(detected: false, confidence: 0.15f),
        };

        var result = await manager.CalibrateAsync(wakeWord.Id, detections, CancellationToken.None);
        var stored = await store.GetAsync(wakeWord.Id, CancellationToken.None);

        Assert.NotNull(stored);
        Assert.True(result.BoostScore > wakeWord.BoostScore);
        Assert.True(result.Threshold < wakeWord.Threshold);
        Assert.Equal(result.BoostScore, stored.BoostScore);
        Assert.Equal(result.Threshold, stored.Threshold);
    }

    [Fact]
    public async Task Calibrate_GeneratesRecommendation()
    {
        var (manager, _) = CreateManager();
        var wakeWord = await manager.RegisterWakeWordAsync("Hey Jarvis");
        var detections = new[]
        {
            Detection(detected: true, confidence: 0.48f),
            Detection(detected: true, confidence: 0.44f),
            Detection(detected: true, confidence: 0.40f),
            Detection(detected: false, confidence: 0.20f),
            Detection(detected: false, confidence: 0.18f),
        };

        var result = await manager.CalibrateAsync(wakeWord.Id, detections, CancellationToken.None);

        Assert.Equal(
            "Fair — try speaking more clearly or reducing background noise",
            result.Recommendation);
    }

    [Fact]
    public async Task ReloadKeywords_WritesFile()
    {
        var (manager, _) = CreateManager();
        var wakeWord = await manager.RegisterWakeWordAsync("Hey Jarvis");

        await manager.ReloadKeywordsAsync(CancellationToken.None);

        Assert.True(File.Exists(_keywordsFilePath));
        var content = await File.ReadAllTextAsync(_keywordsFilePath);
        Assert.Equal($"{wakeWord.Tokens} :{wakeWord.BoostScore:F2} #{wakeWord.Threshold:F2}", content);
    }

    [Fact]
    public void Constructor_EmptyModelPath_DoesNotThrow_AndIsNotReady()
    {
        var exception = Record.Exception(() => CreateManager(modelPath: string.Empty, loadTokenizer: false));

        Assert.Null(exception);

        var (manager, _) = CreateManager(modelPath: string.Empty, loadTokenizer: false);
        Assert.False(manager.IsReady);
    }

    [Fact]
    public async Task RegisterWakeWord_WhenManagerNotConfigured_ThrowsHelpfulError()
    {
        var (manager, _) = CreateManager(modelPath: string.Empty, loadTokenizer: false);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.RegisterWakeWordAsync("Hey Jarvis"));

        Assert.Contains("Wyoming:Models:WakeWord:ModelPath", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReloadKeywords_WhenManagerNotConfigured_DoesNotThrow()
    {
        var (manager, _) = CreateManager(modelPath: string.Empty, loadTokenizer: false);

        await manager.ReloadKeywordsAsync(CancellationToken.None);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    private (CustomWakeWordManager Manager, InMemoryWakeWordStore Store) CreateManager(
        string? modelPath = null,
        bool loadTokenizer = true)
    {
        var store = new InMemoryWakeWordStore();
        var tokenizer = new WakeWordTokenizer();
        if (loadTokenizer)
        {
            tokenizer.LoadVocabulary(_tokensFilePath);
        }

        var manager = new CustomWakeWordManager(
            store,
            tokenizer,
            Options.Create(new WakeWordOptions
            {
                ModelPath = modelPath ?? _modelPath,
                KeywordsFile = "keywords.txt",
            }),
            NullLogger<CustomWakeWordManager>.Instance);

        return (manager, store);
    }

    private static CalibrationDetection Detection(bool detected, float confidence) => new()
    {
        Detected = detected,
        Confidence = confidence,
        AudioDurationMs = 1000,
    };
}
