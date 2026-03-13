using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SherpaOnnx;

namespace lucia.Wyoming.Diarization;

/// <summary>
/// Speaker verification engine using sherpa-onnx speaker embedding extraction.
/// Extracts embeddings from audio and compares against enrolled speaker profiles.
/// </summary>
public sealed class SherpaDiarizationEngine : IDiarizationEngine
{
    private SpeakerEmbeddingExtractor? _extractor;
    private readonly ILogger<SherpaDiarizationEngine> _logger;

    public bool IsReady { get; }

    public SherpaDiarizationEngine(
        IOptions<DiarizationOptions> options,
        ILogger<SherpaDiarizationEngine> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;

        var opts = options.Value;
        if (!opts.Enabled || string.IsNullOrWhiteSpace(opts.EmbeddingModelPath))
        {
            _logger.LogInformation("Speaker verification disabled or embedding model not configured");
            IsReady = false;
            return;
        }

        try
        {
            var config = new SpeakerEmbeddingExtractorConfig
            {
                Model = opts.EmbeddingModelPath,
                NumThreads = 2,
                Provider = "cpu",
            };

            _extractor = new SpeakerEmbeddingExtractor(config);
            IsReady = true;

            _logger.LogInformation(
                "Speaker verification engine initialized with model at {Path}",
                opts.EmbeddingModelPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize speaker verification engine");
            IsReady = false;
        }
    }

    public SpeakerEmbedding ExtractEmbedding(ReadOnlySpan<float> audioSamples, int sampleRate)
    {
        ObjectDisposedException.ThrowIf(_extractor is null, this);

        if (!IsReady)
        {
            throw new InvalidOperationException("Speaker verification engine is not ready.");
        }

        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Sample rate must be positive.");
        }

        if (audioSamples.IsEmpty)
        {
            throw new ArgumentException("Audio samples cannot be empty.", nameof(audioSamples));
        }

        using var stream = _extractor.CreateStream();
        stream.AcceptWaveform(sampleRate, audioSamples.ToArray());
        stream.InputFinished();

        var embedding = _extractor.Compute(stream);

        return new SpeakerEmbedding
        {
            Vector = embedding,
            Duration = TimeSpan.FromSeconds((double)audioSamples.Length / sampleRate),
        };
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
                continue;
            }

            var profileEmbedding = new SpeakerEmbedding
            {
                Vector = profile.AverageEmbedding,
            };

            var similarity = embedding.CosineSimilarity(profileEmbedding);
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
        _extractor?.Dispose();
        _extractor = null;
    }
}
