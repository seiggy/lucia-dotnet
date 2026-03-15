using System.Diagnostics;
using lucia.Wyoming.Audio;
using lucia.Wyoming.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SherpaOnnx;

namespace lucia.Wyoming.Stt;

/// <summary>
/// Offline (non-streaming) STT engine using sherpa-onnx OfflineRecognizer.
/// Supports NeMo Parakeet TDT/CTC, Whisper, SenseVoice, and other offline models.
/// Much faster than manual ONNX Runtime decoding — uses native C++ inference.
/// </summary>
public sealed class SherpaOfflineSttEngine : IGraniteEngine, IDisposable
{
    private readonly ILogger<SherpaOfflineSttEngine> _logger;
    private readonly IModelChangeNotifier _modelChangeNotifier;
    private readonly OfflineSttOptions _options;
    private readonly object _lock = new();
    private readonly List<OfflineRecognizer> _retiredRecognizers = [];
    private OfflineRecognizer? _recognizer;

    public bool IsReady { get; private set; }

    public SherpaOfflineSttEngine(
        IOptions<OfflineSttOptions> options,
        IModelChangeNotifier modelChangeNotifier,
        ILogger<SherpaOfflineSttEngine> logger)
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

    public Task<GraniteTranscript> TranscribeAsync(
        float[] audio,
        int sampleRate,
        IReadOnlyList<KeywordBias>? keywordBias = null,
        CancellationToken ct = default)
    {
        if (!IsReady)
            throw new InvalidOperationException("Offline STT engine is not ready.");

        return Task.Run(() => TranscribeCore(audio, sampleRate), ct);
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

    private GraniteTranscript TranscribeCore(float[] audio, int sampleRate)
    {
        var sw = Stopwatch.StartNew();

        lock (_lock)
        {
            if (!IsReady || _recognizer is null)
                return new GraniteTranscript();

            if (sampleRate != _options.SampleRate)
                audio = AudioResampler.Resample(audio.AsSpan(), sampleRate, _options.SampleRate);

            using var stream = _recognizer.CreateStream();
            stream.AcceptWaveform(_options.SampleRate, audio);
            _recognizer.Decode(stream);

            var text = stream.Result.Text.Trim();
            sw.Stop();

            _logger.LogDebug(
                "Offline STT transcribed {AudioMs}ms audio \u2192 \"{Text}\" in {InferenceMs}ms",
                audio.Length * 1000 / _options.SampleRate,
                text, sw.ElapsedMilliseconds);

            return new GraniteTranscript
            {
                Text = text,
                Confidence = 1.0f,
                InferenceDuration = sw.Elapsed,
            };
        }
    }

    private void TryLoadModel(string modelPath)
    {
        if (string.IsNullOrWhiteSpace(modelPath) || !Directory.Exists(modelPath))
        {
            _logger.LogInformation("Offline STT model path not configured or missing: {Path}",
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

                _logger.LogInformation("Offline STT engine loaded model from {ModelPath}", modelPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load offline STT model from {Path}", modelPath);
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

        // Detect model architecture from available files
        var encoderFile = FindFirstMatch(modelPath, "*encoder*.onnx");
        var decoderFile = FindFirstMatch(modelPath, "*decoder*.onnx");
        var joinerFile = FindFirstMatch(modelPath, "*joiner*.onnx");

        // Transducer: encoder + decoder + joiner (Parakeet TDT, conformer transducer)
        if (encoderFile is not null && decoderFile is not null && joinerFile is not null)
        {
            config.ModelConfig.Transducer.Encoder = encoderFile;
            config.ModelConfig.Transducer.Decoder = decoderFile;
            config.ModelConfig.Transducer.Joiner = joinerFile;

            _logger.LogDebug("Detected transducer model: {Encoder}", Path.GetFileName(encoderFile));
            return config;
        }

        // NeMo CTC: single model.onnx or model.int8.onnx
        var ctcModel = FindFirstMatch(modelPath, "model*.onnx");
        if (ctcModel is not null)
        {
            config.ModelConfig.NeMoCtc.Model = ctcModel;
            _logger.LogDebug("Detected NeMo CTC model: {Model}", Path.GetFileName(ctcModel));
            return config;
        }

        // Whisper: encoder + decoder (no joiner)
        if (encoderFile is not null && decoderFile is not null)
        {
            config.ModelConfig.Whisper.Encoder = encoderFile;
            config.ModelConfig.Whisper.Decoder = decoderFile;
            _logger.LogDebug("Detected Whisper model: {Encoder}", Path.GetFileName(encoderFile));
            return config;
        }

        throw new InvalidOperationException(
            $"Could not detect a supported offline model architecture under '{modelPath}'.");
    }

    private void OnActiveModelChanged(ActiveModelChangedEvent evt)
    {
        if (evt.EngineType != EngineType.OfflineStt) return;
        _logger.LogInformation("Reloading offline STT engine with model {ModelId}", evt.ModelId);
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
