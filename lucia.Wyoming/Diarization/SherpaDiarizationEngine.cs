using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SherpaOnnx;
using lucia.Wyoming.Models;

namespace lucia.Wyoming.Diarization;

/// <summary>
/// Speaker verification engine using sherpa-onnx speaker embedding extraction.
/// Extracts embeddings from audio and compares against enrolled speaker profiles.
/// </summary>
public sealed class SherpaDiarizationEngine : IDiarizationEngine, IDisposable
{
    private readonly ILogger<SherpaDiarizationEngine> _logger;
    private readonly IModelChangeNotifier _modelChangeNotifier;
    private readonly OnnxProviderDetector _providerDetector;
    private readonly object _lock = new();
    private SpeakerEmbeddingExtractor? _extractor;

    public bool IsReady => _extractor is not null;

    public SherpaDiarizationEngine(
        IOptions<DiarizationOptions> options,
        IModelChangeNotifier modelChangeNotifier,
        OnnxProviderDetector providerDetector,
        ILogger<SherpaDiarizationEngine> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(modelChangeNotifier);
        ArgumentNullException.ThrowIfNull(providerDetector);
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
        _modelChangeNotifier = modelChangeNotifier;
        _providerDetector = providerDetector;

        var opts = options.Value;
        if (opts.Enabled && !string.IsNullOrWhiteSpace(opts.EmbeddingModelPath))
        {
            TryLoadModel(opts.EmbeddingModelPath);
        }
        else
        {
            _logger.LogInformation("Speaker verification waiting for model activation via event");
        }

        _modelChangeNotifier.ActiveModelChanged += OnActiveModelChanged;
    }

    public SpeakerEmbedding ExtractEmbedding(ReadOnlySpan<float> audioSamples, int sampleRate)
    {
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Sample rate must be positive.");
        }

        if (audioSamples.IsEmpty)
        {
            throw new ArgumentException("Audio samples cannot be empty.", nameof(audioSamples));
        }

        var samples = audioSamples.ToArray();

        lock (_lock)
        {
            if (_extractor is null)
            {
                throw new InvalidOperationException("Speaker verification engine is not ready.");
            }

            using var stream = _extractor.CreateStream();
            stream.AcceptWaveform(sampleRate, samples);
            stream.InputFinished();

            var embedding = _extractor.Compute(stream);

            return new SpeakerEmbedding
            {
                Vector = embedding,
                Duration = TimeSpan.FromSeconds((double)samples.Length / sampleRate),
            };
        }
    }

    public SpeakerIdentification? IdentifySpeaker(
        SpeakerEmbedding embedding,
        IReadOnlyList<SpeakerProfile> enrolledProfiles,
        float threshold = 0.7f)
    {
        ArgumentNullException.ThrowIfNull(embedding);
        ArgumentNullException.ThrowIfNull(enrolledProfiles);

        SpeakerIdentification? best = null;

        foreach (var profile in enrolledProfiles)
        {
            if (profile.AverageEmbedding.Length == 0)
            {
                _logger.LogDebug(
                    "Skipping profile {ProfileId} ({Name}) — empty embedding",
                    profile.Id, profile.Name);
                continue;
            }

            if (profile.AverageEmbedding.Length != embedding.Vector.Length)
            {
                _logger.LogWarning(
                    "Skipping profile {ProfileId} ({Name}) — embedding dimension mismatch (profile={ProfileDim}, current={CurrentDim}). Re-enroll this profile to fix.",
                    profile.Id, profile.Name, profile.AverageEmbedding.Length, embedding.Vector.Length);
                continue;
            }

            var profileEmbedding = new SpeakerEmbedding
            {
                Vector = profile.AverageEmbedding,
            };

            var similarity = embedding.CosineSimilarity(profileEmbedding);

            _logger.LogDebug(
                "Profile {ProfileId} ({Name}) similarity={Similarity:F3} (threshold={Threshold:F2})",
                profile.Id, profile.Name, similarity, threshold);

            if (similarity < threshold || (best is not null && similarity <= best.Similarity))
            {
                continue;
            }

            best = new SpeakerIdentification
            {
                ProfileId = profile.Id,
                Name = profile.Name,
                Similarity = similarity,
                IsAuthorized = profile.IsAuthorized,
            };
        }

        return best;
    }

    public void Dispose()
    {
        _modelChangeNotifier.ActiveModelChanged -= OnActiveModelChanged;

        lock (_lock)
        {
            _extractor?.Dispose();
            _extractor = null;
        }
    }

    private void OnActiveModelChanged(ActiveModelChangedEvent evt)
    {
        if (evt.EngineType != EngineType.SpeakerEmbedding) return;

        _logger.LogInformation("Reloading diarization engine with model {ModelId}", evt.ModelId);
        TryLoadModel(evt.ModelPath);
    }

    private void TryLoadModel(string modelPath)
    {
        if (string.IsNullOrWhiteSpace(modelPath))
        {
            _logger.LogWarning("Diarization model path is empty");
            return;
        }

        // The model path may be a directory (from ActiveModelChanged events)
        // or a direct file path (from legacy EmbeddingModelPath config).
        var onnxFile = FindOnnxModel(modelPath) ?? (File.Exists(modelPath) ? modelPath : null);
        if (onnxFile is null)
        {
            _logger.LogWarning("No .onnx model found in {ModelPath}", modelPath);
            return;
        }

        lock (_lock)
        {
            try
            {
                var config = new SpeakerEmbeddingExtractorConfig
                {
                    Model = onnxFile,
                    NumThreads = 2,
                    Provider = _providerDetector.BestSherpaProvider,
                };

                var newExtractor = new SpeakerEmbeddingExtractor(config);
                var oldExtractor = _extractor;

                _extractor = newExtractor;
                oldExtractor?.Dispose();

                _logger.LogInformation(
                    "Speaker verification engine loaded model from {Path}",
                    onnxFile);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load speaker verification model from {Path}", onnxFile);
            }
        }
    }

    private static string? FindOnnxModel(string modelDirectory)
    {
        if (!Directory.Exists(modelDirectory)) return null;
        return Directory.EnumerateFiles(modelDirectory, "*.onnx", SearchOption.AllDirectories).FirstOrDefault();
    }
}
