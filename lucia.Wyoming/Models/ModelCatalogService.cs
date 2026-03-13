using Microsoft.Extensions.Options;

namespace lucia.Wyoming.Models;

public sealed class ModelCatalogService(IOptionsMonitor<SttModelOptions> optionsMonitor)
{
    private static readonly IReadOnlyList<AsrModelDefinition> BuiltInCatalog =
    [
        CreateDefinition(
            "sherpa-onnx-streaming-zipformer-en-2023-06-26",
            "Streaming Zipformer English",
            ModelArchitecture.ZipformerTransducer,
            isStreaming: true,
            languages: ["en"],
            sizeBytes: 80_000_000,
            description: "Default English streaming model with a strong speed and accuracy balance.",
            isDefault: true,
            minMemoryMb: 200),
        CreateDefinition(
            "sherpa-onnx-streaming-zipformer-en-20M-2023-02-17",
            "Streaming Zipformer English 20M",
            ModelArchitecture.ZipformerTransducer,
            isStreaming: true,
            languages: ["en"],
            sizeBytes: 20_000_000,
            description: "Compact English streaming model for resource-constrained devices.",
            minMemoryMb: 96),
        CreateDefinition(
            "sherpa-onnx-streaming-zipformer-bilingual-zh-en-2023-02-20",
            "Streaming Zipformer Chinese + English",
            ModelArchitecture.ZipformerTransducer,
            isStreaming: true,
            languages: ["zh", "en"],
            sizeBytes: 120_000_000,
            description: "Bilingual Chinese and English streaming zipformer model.",
            minMemoryMb: 256),
        CreateDefinition(
            "sherpa-onnx-streaming-zipformer-fr-2023-04-14",
            "Streaming Zipformer French",
            ModelArchitecture.ZipformerTransducer,
            isStreaming: true,
            languages: ["fr"],
            sizeBytes: 80_000_000,
            description: "French streaming zipformer model for low-latency recognition.",
            minMemoryMb: 200),
        CreateDefinition(
            "sherpa-onnx-streaming-zipformer-ar_en_id_ja_ru_th_vi_zh-2025-02-10",
            "Streaming Zipformer 8-Language",
            ModelArchitecture.ZipformerTransducer,
            isStreaming: true,
            languages: ["ar", "en", "id", "ja", "ru", "th", "vi", "zh"],
            sizeBytes: 200_000_000,
            description: "Multilingual streaming model covering Arabic, English, Indonesian, Japanese, Russian, Thai, Vietnamese, and Chinese.",
            minMemoryMb: 384),
        CreateDefinition(
            "sherpa-onnx-streaming-zipformer-ctc-small-2024-03-18",
            "Streaming Zipformer CTC Small",
            ModelArchitecture.ZipformerCtc,
            isStreaming: true,
            languages: ["en"],
            sizeBytes: 40_000_000,
            description: "Small English streaming CTC variant for CPU-friendly deployments.",
            minMemoryMb: 128),
        CreateDefinition(
            "sherpa-onnx-streaming-paraformer-bilingual-zh-en",
            "Streaming Paraformer Chinese + English",
            ModelArchitecture.Paraformer,
            isStreaming: true,
            languages: ["zh", "en"],
            sizeBytes: 150_000_000,
            description: "Streaming paraformer model for Chinese and English speech recognition.",
            minMemoryMb: 320),
        CreateDefinition(
            "sherpa-onnx-streaming-conformer-en-2023-05-09",
            "Streaming Conformer English",
            ModelArchitecture.Conformer,
            isStreaming: true,
            languages: ["en"],
            sizeBytes: 100_000_000,
            description: "Streaming conformer model tuned for English transcription.",
            minMemoryMb: 256),
        CreateDefinition(
            "sherpa-onnx-nemo-streaming-fast-conformer-ctc-en-80ms",
            "NeMo FastConformer CTC English 80ms",
            ModelArchitecture.NemoFastConformer,
            isStreaming: true,
            languages: ["en"],
            sizeBytes: 100_000_000,
            description: "Low-latency NeMo FastConformer CTC model with 80ms chunking.",
            minMemoryMb: 256),
        CreateDefinition(
            "sherpa-onnx-nemo-streaming-fast-conformer-transducer-en-80ms",
            "NeMo FastConformer Transducer English 80ms",
            ModelArchitecture.NemoFastConformer,
            isStreaming: true,
            languages: ["en"],
            sizeBytes: 120_000_000,
            description: "Low-latency NeMo FastConformer transducer model for streaming English audio.",
            minMemoryMb: 320),
        CreateDefinition(
            "sherpa-onnx-whisper-tiny.en",
            "Whisper Tiny English",
            ModelArchitecture.Whisper,
            isStreaming: false,
            languages: ["en"],
            sizeBytes: 75_000_000,
            description: "Fastest English-only Whisper model for offline transcription.",
            minMemoryMb: 256),
        CreateDefinition(
            "sherpa-onnx-whisper-small.en",
            "Whisper Small English",
            ModelArchitecture.Whisper,
            isStreaming: false,
            languages: ["en"],
            sizeBytes: 460_000_000,
            description: "More accurate English Whisper model for offline transcription workloads.",
            minMemoryMb: 1024),
        CreateDefinition(
            "sherpa-onnx-whisper-tiny",
            "Whisper Tiny Multilingual",
            ModelArchitecture.Whisper,
            isStreaming: false,
            languages: ["mul"],
            sizeBytes: 75_000_000,
            description: "Small multilingual Whisper model for offline recognition across many languages.",
            minMemoryMb: 256),
        CreateDefinition(
            "sherpa-onnx-sense-voice-zh-en-ja-ko-yue-2025-09-09",
            "SenseVoice 5-Language",
            ModelArchitecture.SenseVoice,
            isStreaming: false,
            languages: ["zh", "en", "ja", "ko", "yue"],
            sizeBytes: 200_000_000,
            description: "Offline SenseVoice model covering Mandarin, English, Japanese, Korean, and Cantonese.",
            minMemoryMb: 512),
        CreateDefinition(
            "sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8",
            "NeMo Parakeet TDT 0.6B Int8",
            ModelArchitecture.NemoParakeet,
            isStreaming: false,
            languages: ["en"],
            sizeBytes: 300_000_000,
            description: "Quantized NeMo Parakeet model for higher-accuracy offline English transcription.",
            minMemoryMb: 768),
        CreateDefinition(
            "sherpa-onnx-nemo-canary-180m-flash-en-es-de-fr",
            "NeMo Canary Flash 4-Language",
            ModelArchitecture.NemoCanary,
            isStreaming: false,
            languages: ["en", "es", "de", "fr"],
            sizeBytes: 180_000_000,
            description: "Offline NeMo Canary model for English, Spanish, German, and French.",
            minMemoryMb: 512),
        CreateDefinition(
            "sherpa-onnx-nemotron-speech-streaming-en-0.6b-int8-2026-01-14",
            "Nemotron Speech Streaming English 0.6B Int8",
            ModelArchitecture.NemoNemotron,
            isStreaming: true,
            languages: ["en"],
            sizeBytes: 600_000_000,
            description: "Large streaming English Nemotron model prioritizing accuracy.",
            minMemoryMb: 1536),
    ];

    public IReadOnlyList<AsrModelDefinition> GetAvailableModels(ModelFilter? filter = null)
    {
        IEnumerable<AsrModelDefinition> models = BuiltInCatalog;

        if (filter?.StreamingOnly is true)
        {
            models = models.Where(static model => model.IsStreaming);
        }

        if (!string.IsNullOrWhiteSpace(filter?.Language))
        {
            var language = filter.Language.Trim();
            models = models.Where(model => model.Languages.Contains(language, StringComparer.OrdinalIgnoreCase));
        }

        if (filter?.Architecture is { } architecture)
        {
            models = models.Where(model => model.Architecture == architecture);
        }

        if (filter?.MaxSizeMb is { } maxSizeMb)
        {
            var maxSizeBytes = maxSizeMb * 1_000_000L;
            models = models.Where(model => model.SizeBytes <= maxSizeBytes);
        }

        if (filter?.InstalledOnly is true)
        {
            models = models.Where(model => IsModelInstalled(model.Id));
        }

        return models
            .OrderByDescending(static model => model.IsDefault)
            .ThenBy(static model => model.Name, StringComparer.Ordinal)
            .ToArray();
    }

    public IReadOnlyList<AsrModelDefinition> GetInstalledModels() =>
        BuiltInCatalog
            .Where(model => IsModelInstalled(model.Id))
            .OrderByDescending(static model => model.IsDefault)
            .ThenBy(static model => model.Name, StringComparer.Ordinal)
            .ToArray();

    public AsrModelDefinition? GetModelById(string id) =>
        BuiltInCatalog.FirstOrDefault(model => string.Equals(model.Id, id, StringComparison.Ordinal));

    private bool IsModelInstalled(string modelId)
    {
        var modelPath = Path.Combine(optionsMonitor.CurrentValue.ModelBasePath, modelId);
        return Directory.Exists(modelPath)
            && Directory.EnumerateFiles(modelPath, "*.onnx", SearchOption.AllDirectories).Any();
    }

    private static AsrModelDefinition CreateDefinition(
        string id,
        string name,
        ModelArchitecture architecture,
        bool isStreaming,
        string[] languages,
        long sizeBytes,
        string description,
        bool isDefault = false,
        int minMemoryMb = 256) =>
        new()
        {
            Id = id,
            Name = name,
            Architecture = architecture,
            IsStreaming = isStreaming,
            Languages = languages,
            SizeBytes = sizeBytes,
            Description = description,
            DownloadUrl = $"https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/{id}.tar.bz2",
            IsDefault = isDefault,
            MinMemoryMb = minMemoryMb,
        };
}
