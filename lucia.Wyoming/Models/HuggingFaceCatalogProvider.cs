using Microsoft.Extensions.Logging;

namespace lucia.Wyoming.Models;

/// <summary>
/// Catalog provider that discovers ONNX ASR models from the onnx-community organization on Hugging Face Hub.
/// Only active when <see cref="HuggingFaceOptions.ApiToken"/> is configured.
/// </summary>
public sealed class HuggingFaceCatalogProvider(
    HuggingFaceClient client,
    ILogger<HuggingFaceCatalogProvider> logger) : IModelCatalogProvider
{
    public ModelSource Source => ModelSource.HuggingFace;

    public async Task<IReadOnlyList<WyomingModelDefinition>> GetModelsAsync(
        EngineType engineType,
        CancellationToken ct = default)
    {
        var models = await client.SearchModelsAsync(engineType, ct);

        if (models.Count == 0)
        {
            return [];
        }

        var definitions = new List<WyomingModelDefinition>(models.Count);

        foreach (var model in models)
        {
            var definition = MapToDefinition(model, engineType);
            if (definition is not null)
            {
                definitions.Add(definition);
            }
        }

        logger.LogDebug(
            "Discovered {Count} HuggingFace models for {EngineType}",
            definitions.Count,
            engineType);

        return definitions;
    }

    public async Task<WyomingModelDefinition?> GetModelByIdAsync(
        EngineType engineType,
        string modelId,
        CancellationToken ct = default)
    {
        // modelId for HF models is the repo ID (e.g. "onnx-community/whisper-tiny")
        var info = await client.GetModelInfoAsync(modelId, ct);

        return info is not null ? MapToDefinition(info, engineType) : null;
    }

    private static WyomingModelDefinition? MapToDefinition(
        HuggingFaceModelInfo info,
        EngineType engineType)
    {
        var repoId = info.Id;
        if (string.IsNullOrWhiteSpace(repoId))
        {
            return null;
        }

        var name = repoId.Contains('/')
            ? repoId[(repoId.IndexOf('/') + 1)..]
            : repoId;

        var languages = ExtractLanguages(info.Tags);

        return new WyomingModelDefinition
        {
            Id = repoId,
            Name = FormatDisplayName(name),
            EngineType = engineType,
            Description = $"ONNX model from Hugging Face ({info.Downloads:N0} downloads)",
            Languages = languages.Length > 0 ? languages : ["mul"],
            SizeBytes = 0,
            DownloadUrl = $"https://huggingface.co/{repoId}",
            IsDefault = false,
            MinMemoryMb = 0,
            IsArchive = false,
            Source = ModelSource.HuggingFace,
            RepoId = repoId,
            LastModified = info.LastModified,
        };
    }

    private static string FormatDisplayName(string repoName)
    {
        // "whisper-tiny-ONNX" → "Whisper Tiny ONNX"
        return string.Join(' ', repoName
            .Replace('-', ' ')
            .Replace('_', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(static word => word.Length <= 4 && word.All(static c => char.IsUpper(c) || char.IsDigit(c))
                ? word
                : char.ToUpperInvariant(word[0]) + word[1..]));
    }

    private static string[] ExtractLanguages(string[] tags)
    {
        // HF tags include ISO language codes directly (e.g., "en", "fr", "de")
        var knownLanguages = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ar", "de", "en", "es", "fr", "hi", "it", "ja", "ko", "nl", "pt", "ru", "sv", "th", "vi", "zh", "yue",
        };

        return tags
            .Where(tag => knownLanguages.Contains(tag))
            .ToArray();
    }
}
