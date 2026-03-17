using lucia.Wyoming.Audio;
using lucia.Wyoming.Diarization;
using lucia.Wyoming.Vad;
using lucia.Wyoming.WakeWord;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucia.Wyoming.Models;

/// <summary>
/// Aggregates model catalogs from multiple providers (sherpa-onnx built-in + Hugging Face).
/// </summary>
public sealed class ModelCatalogService(
    IEnumerable<IModelCatalogProvider> providers,
    IOptionsMonitor<SttModelOptions> sttOptions,
    IOptionsMonitor<VadOptions> vadOptions,
    IOptionsMonitor<WakeWordOptions> wakeWordOptions,
    IOptionsMonitor<DiarizationOptions> diarizationOptions,
    IOptionsMonitor<SpeechEnhancementOptions> enhancementOptions,
    ILogger<ModelCatalogService> logger)
{
    public IReadOnlyList<AsrModelDefinition> GetAvailableModels(ModelFilter? filter = null)
    {
        // Synchronous path: only uses the sherpa-onnx provider (always available)
        var sherpaProvider = providers.FirstOrDefault(p => p.Source == ModelSource.SherpaOnnx);
        if (sherpaProvider is null)
        {
            return [];
        }

        var models = sherpaProvider
            .GetModelsAsync(EngineType.Stt)
            .GetAwaiter()
            .GetResult()
            .OfType<AsrModelDefinition>();

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

    public IReadOnlyList<AsrModelDefinition> GetInstalledModels()
    {
        var sherpaProvider = providers.FirstOrDefault(p => p.Source == ModelSource.SherpaOnnx);
        if (sherpaProvider is null)
        {
            return [];
        }

        return sherpaProvider
            .GetModelsAsync(EngineType.Stt)
            .GetAwaiter()
            .GetResult()
            .OfType<AsrModelDefinition>()
            .Where(model => IsModelInstalled(model.Id))
            .OrderByDescending(static model => model.IsDefault)
            .ThenBy(static model => model.Name, StringComparer.Ordinal)
            .ToArray();
    }

    public AsrModelDefinition? GetModelById(string id) =>
        GetModelById(EngineType.Stt, id) as AsrModelDefinition;

    public IReadOnlyList<WyomingModelDefinition> GetAvailableModels(EngineType engineType) =>
        GetAvailableModelsSync(engineType);

    /// <summary>
    /// Returns available models from all providers, including async ones like Hugging Face.
    /// </summary>
    public async Task<IReadOnlyList<WyomingModelDefinition>> GetAvailableModelsAsync(
        EngineType engineType,
        CancellationToken ct = default)
    {
        var allModels = new List<WyomingModelDefinition>();

        foreach (var provider in providers)
        {
            try
            {
                var models = await provider.GetModelsAsync(engineType, ct);
                allModels.AddRange(models);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to query models from {Source} provider", provider.Source);
            }
        }

        return allModels
            .OrderByDescending(static model => model.IsDefault)
            .ThenBy(static model => model.Name, StringComparer.Ordinal)
            .ToArray();
    }

    public IReadOnlyList<WyomingModelDefinition> GetInstalledModels(EngineType engineType) =>
        GetAvailableModelsSync(engineType)
            .Where(model => IsModelInstalled(engineType, model.Id))
            .OrderByDescending(static model => model.IsDefault)
            .ThenBy(static model => model.Name, StringComparer.Ordinal)
            .ToArray();

    public WyomingModelDefinition? GetModelById(EngineType engineType, string id)
    {
        foreach (var provider in providers)
        {
            var model = provider.GetModelByIdAsync(engineType, id)
                .GetAwaiter()
                .GetResult();

            if (model is not null)
            {
                return model;
            }
        }

        return null;
    }

    /// <summary>
    /// Async model lookup across all providers.
    /// </summary>
    public async Task<WyomingModelDefinition?> GetModelByIdAsync(
        EngineType engineType,
        string id,
        CancellationToken ct = default)
    {
        foreach (var provider in providers)
        {
            var model = await provider.GetModelByIdAsync(engineType, id, ct);
            if (model is not null)
            {
                return model;
            }
        }

        return null;
    }

    private IReadOnlyList<WyomingModelDefinition> GetAvailableModelsSync(EngineType engineType)
    {
        var sherpaProvider = providers.FirstOrDefault(p => p.Source == ModelSource.SherpaOnnx);
        return sherpaProvider?.GetModelsAsync(engineType).GetAwaiter().GetResult() ?? [];
    }

    private bool IsModelInstalled(string modelId) =>
        IsModelInstalled(EngineType.Stt, modelId);

    private bool IsModelInstalled(EngineType engineType, string modelId)
    {
        var basePath = GetModelBasePath(engineType);
        var modelPath = Path.Combine(basePath, modelId);
        return Directory.Exists(modelPath)
            && Directory.EnumerateFiles(modelPath, "*.onnx", SearchOption.AllDirectories).Any();
    }

    private string GetModelBasePath(EngineType engineType) =>
        engineType switch
        {
            EngineType.Stt => sttOptions.CurrentValue.ModelBasePath,
            EngineType.OfflineStt => sttOptions.CurrentValue.ModelBasePath,
            EngineType.Vad => vadOptions.CurrentValue.ModelBasePath,
            EngineType.WakeWord => wakeWordOptions.CurrentValue.ModelBasePath,
            EngineType.SpeakerEmbedding => diarizationOptions.CurrentValue.ModelBasePath,
            EngineType.SpeechEnhancement => enhancementOptions.CurrentValue.ModelBasePath,
            _ => throw new ArgumentOutOfRangeException(nameof(engineType)),
        };
}
