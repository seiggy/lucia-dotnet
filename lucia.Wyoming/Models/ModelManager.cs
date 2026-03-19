using lucia.Wyoming.Audio;
using lucia.Wyoming.Diarization;
using lucia.Wyoming.Stt;
using lucia.Wyoming.Vad;
using lucia.Wyoming.WakeWord;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucia.Wyoming.Models;

public sealed class ModelManager(
    IOptionsMonitor<SttModelOptions> sttOptionsMonitor,
    IOptionsMonitor<VadOptions> vadOptionsMonitor,
    IOptionsMonitor<WakeWordOptions> wakeWordOptionsMonitor,
    IOptionsMonitor<DiarizationOptions> diarizationOptionsMonitor,
    IOptionsMonitor<SpeechEnhancementOptions> enhancementOptionsMonitor,
    IOptionsMonitor<HybridSttOptions> hybridSttOptionsMonitor,
    ModelCatalogService catalogService,
    ModelDownloader downloader,
    HuggingFaceModelDownloader hfDownloader,
    HuggingFaceClient hfClient,
    IModelPreferenceStore preferenceStore,
    ILogger<ModelManager> logger) : IModelChangeNotifier
{
    private readonly Dictionary<EngineType, string> _activeModelOverrides = [];
    private volatile bool _preferencesLoaded;

    public event Action<ActiveModelChangedEvent>? ActiveModelChanged;

    /// <summary>
    /// Tracks which STT engine type the user last activated (Stt for streaming, OfflineStt for hybrid/offline).
    /// Used by status and session to determine which engine to prefer when multiple are ready.
    /// </summary>
    public EngineType PreferredSttEngineType { get; private set; } =
        hybridSttOptionsMonitor.CurrentValue.Enabled ? EngineType.OfflineStt : EngineType.Stt;

    /// <summary>
    /// Gets the active STT model ID. Convenience wrapper for <see cref="GetActiveModelId(EngineType)"/>.
    /// </summary>
    public string ActiveModelId => GetActiveModelId(EngineType.Stt);

    public string GetActiveModelId(EngineType engineType)
    {
        // Preferences are loaded asynchronously via InitializeAsync.
        // If not yet loaded, fall back to config defaults.
        return _activeModelOverrides.TryGetValue(engineType, out var overrideId) && !string.IsNullOrWhiteSpace(overrideId)
            ? overrideId
            : GetConfiguredActiveModel(engineType);
    }

    /// <summary>
    /// Loads persisted model preferences from MongoDB. Call during startup.
    /// </summary>
    public async Task LoadPersistedPreferencesAsync(CancellationToken ct = default)
    {
        if (_preferencesLoaded)
            return;

        try
        {
            var overrides = await preferenceStore.LoadOverridesAsync(ct).ConfigureAwait(false);

            foreach (var (engineType, modelId) in overrides)
            {
                _activeModelOverrides[engineType] = modelId;
            }

            if (overrides.ContainsKey(EngineType.OfflineStt))
                PreferredSttEngineType = EngineType.OfflineStt;
            else if (overrides.ContainsKey(EngineType.Stt))
                PreferredSttEngineType = EngineType.Stt;

            logger.LogInformation(
                "Restored {Count} model preference(s) from MongoDB (preferred STT: {PreferredStt})",
                overrides.Count, PreferredSttEngineType);

            // Notify engines so they load the restored models
            foreach (var (engineType, modelId) in overrides)
            {
                var modelBasePath = GetModelBasePath(engineType);
                var modelDirectory = GetSafeModelDirectory(modelId, modelBasePath);

                if (IsUsableModelDirectory(modelDirectory))
                {
                    logger.LogInformation(
                        "Activating restored {EngineType} model {ModelId} at {Path}",
                        engineType, modelId, modelDirectory);

                    ActiveModelChanged?.Invoke(new ActiveModelChangedEvent
                    {
                        EngineType = engineType,
                        ModelId = modelId,
                        ModelPath = modelDirectory,
                    });
                }
                else
                {
                    logger.LogWarning(
                        "Restored preference for {EngineType}/{ModelId} but model directory not found at {Path} — skipping activation",
                        engineType, modelId, modelDirectory);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load model preferences — using config defaults");
        }
        finally
        {
            _preferencesLoaded = true;
        }
    }

    public async Task<bool> ValidateActiveModelAsync(CancellationToken ct = default)
    {
        var activeModelId = GetActiveModelId(EngineType.Stt);
        var modelBasePath = sttOptionsMonitor.CurrentValue.ModelBasePath;
        var modelDirectory = GetSafeModelDirectory(activeModelId, modelBasePath);

        if (IsUsableModelDirectory(modelDirectory))
        {
            return DetectModelArchitecture(modelDirectory) is not ModelArchitecture.Unknown;
        }

        var modelDefinition = catalogService.GetModelById(activeModelId);
        if (modelDefinition is null || !modelDefinition.IsDefault || !sttOptionsMonitor.CurrentValue.AutoDownloadDefault)
        {
            logger.LogWarning("Active Wyoming model {ModelId} is not installed at {ModelDirectory}", activeModelId, modelDirectory);
            return false;
        }

        var result = await downloader
            .DownloadModelAsync(modelDefinition, modelBasePath, ct: ct)
            .ConfigureAwait(false);

        if (!result.Success || string.IsNullOrWhiteSpace(result.LocalPath))
        {
            logger.LogWarning("Failed to auto-download default Wyoming model {ModelId}: {Error}", activeModelId, result.Error);
            return false;
        }

        return DetectModelArchitecture(result.LocalPath) is not ModelArchitecture.Unknown;
    }

    public ModelArchitecture DetectModelArchitecture(string modelDir)
    {
        if (!Directory.Exists(modelDir))
        {
            return ModelArchitecture.Unknown;
        }

        var modelName = Path.GetFileName(modelDir) ?? string.Empty;
        var allFiles = Directory
            .EnumerateFiles(modelDir, "*", SearchOption.AllDirectories)
            .Select(static filePath => Path.GetFileName(filePath) ?? string.Empty)
            .Where(static fileName => fileName.Length > 0)
            .ToArray();

        if (ContainsFile(allFiles, "ctc.onnx") || modelName.Contains("ctc", StringComparison.OrdinalIgnoreCase))
        {
            return ModelArchitecture.ZipformerCtc;
        }

        if (ContainsAnyFile(allFiles, "encoder.onnx", "decoder.onnx", "joiner.onnx")
            || allFiles.Any(static fileName => fileName.Contains("-encoder.onnx", StringComparison.OrdinalIgnoreCase))
            || modelName.Contains("zipformer", StringComparison.OrdinalIgnoreCase))
        {
            if (modelName.Contains("whisper", StringComparison.OrdinalIgnoreCase)
                || allFiles.Any(static fileName => fileName.Contains("whisper", StringComparison.OrdinalIgnoreCase)))
            {
                return ModelArchitecture.Whisper;
            }

            return ModelArchitecture.ZipformerTransducer;
        }

        if (modelName.Contains("sense-voice", StringComparison.OrdinalIgnoreCase)
            || modelName.Contains("sensevoice", StringComparison.OrdinalIgnoreCase))
        {
            return ModelArchitecture.SenseVoice;
        }

        if (modelName.Contains("parakeet", StringComparison.OrdinalIgnoreCase))
        {
            return ModelArchitecture.NemoParakeet;
        }

        if (modelName.Contains("canary", StringComparison.OrdinalIgnoreCase))
        {
            return ModelArchitecture.NemoCanary;
        }

        if (modelName.Contains("nemotron", StringComparison.OrdinalIgnoreCase))
        {
            return ModelArchitecture.NemoNemotron;
        }

        if (modelName.Contains("fast-conformer", StringComparison.OrdinalIgnoreCase)
            || modelName.Contains("nemo", StringComparison.OrdinalIgnoreCase))
        {
            return ModelArchitecture.NemoFastConformer;
        }

        if (modelName.Contains("conformer", StringComparison.OrdinalIgnoreCase))
        {
            return ModelArchitecture.Conformer;
        }

        if (modelName.Contains("paraformer", StringComparison.OrdinalIgnoreCase)
            || (ContainsFile(allFiles, "model.onnx") && ContainsFile(allFiles, "tokens.txt")))
        {
            return ModelArchitecture.Paraformer;
        }

        if (modelName.Contains("whisper", StringComparison.OrdinalIgnoreCase)
            || allFiles.Any(static fileName => fileName.Contains("-encoder.onnx", StringComparison.OrdinalIgnoreCase)))
        {
            return ModelArchitecture.Whisper;
        }

        if (modelName.Contains("telespeech", StringComparison.OrdinalIgnoreCase))
        {
            return ModelArchitecture.Telespeech;
        }

        if (modelName.Contains("lstm", StringComparison.OrdinalIgnoreCase))
        {
            return ModelArchitecture.Lstm;
        }

        return ModelArchitecture.Unknown;
    }

    /// <summary>
    /// Switches the active model, auto-detecting the engine type from the catalog.
    /// Falls back to <see cref="EngineType.Stt"/> if the model is not found in any catalog.
    /// </summary>
    public Task<bool> SwitchActiveModelAsync(string modelId, CancellationToken ct = default)
    {
        var model = catalogService.GetModelById(modelId);
        var engineType = model?.EngineType ?? EngineType.Stt;
        return SwitchActiveModelAsync(engineType, modelId, ct);
    }

    public async Task<bool> SwitchActiveModelAsync(EngineType engineType, string modelId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);

        var modelBasePath = GetModelBasePath(engineType);
        var modelDirectory = GetSafeModelDirectory(modelId, modelBasePath);

        if (!IsUsableModelDirectory(modelDirectory))
        {
            var modelDefinition = catalogService.GetModelById(engineType, modelId);
            if (modelDefinition is null)
            {
                throw new DirectoryNotFoundException($"Wyoming model directory '{modelDirectory}' was not found and model '{modelId}' is not in the catalog.");
            }

            var downloadResult = await downloader
                .DownloadModelAsync(modelDefinition, modelBasePath, ct: ct)
                .ConfigureAwait(false);

            if (!downloadResult.Success)
            {
                throw new InvalidOperationException(downloadResult.Error ?? $"Failed to download Wyoming model '{modelId}'.");
            }
        }

        _activeModelOverrides[engineType] = modelId;

        // Persist to MongoDB so the selection survives reboots
        await preferenceStore.SaveOverrideAsync(engineType, modelId, ct).ConfigureAwait(false);

        // Track which STT engine type the user prefers
        if (engineType is EngineType.Stt or EngineType.OfflineStt)
        {
            PreferredSttEngineType = engineType;
        }

        ActiveModelChanged?.Invoke(new ActiveModelChangedEvent
        {
            EngineType = engineType,
            ModelId = modelId,
            ModelPath = modelDirectory,
        });

        logger.LogInformation(
            "Switched active Wyoming {EngineType} model to {ModelId} at {ModelDirectory}",
            engineType,
            modelId,
            modelDirectory);

        return true;
    }

    /// <summary>
    /// Deletes an STT model. Convenience wrapper for <see cref="DeleteModelAsync(EngineType, string, CancellationToken)"/>.
    /// </summary>
    public Task DeleteModelAsync(string modelId, CancellationToken ct = default) =>
        DeleteModelAsync(EngineType.Stt, modelId, ct);

    public async Task DeleteModelAsync(EngineType engineType, string modelId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);

        var modelBasePath = GetModelBasePath(engineType);
        var modelDirectory = GetSafeModelDirectory(modelId, modelBasePath);

        if (!Directory.Exists(modelDirectory))
        {
            return;
        }

        Directory.Delete(modelDirectory, recursive: true);

        if (_activeModelOverrides.TryGetValue(engineType, out var active)
            && string.Equals(active, modelId, StringComparison.Ordinal))
        {
            _activeModelOverrides.Remove(engineType);
            await preferenceStore.RemoveOverrideAsync(engineType, ct).ConfigureAwait(false);
        }

        logger.LogInformation(
            "Deleted Wyoming {EngineType} model {ModelId} from {ModelDirectory}",
            engineType,
            modelId,
            modelDirectory);
    }

    /// <summary>
    /// Checks if a HuggingFace-sourced model has an update available by comparing
    /// the remote lastModified timestamp against local file timestamps.
    /// </summary>
    public async Task<bool> CheckForUpdateAsync(
        EngineType engineType,
        string modelId,
        CancellationToken ct = default)
    {
        var model = catalogService.GetModelById(engineType, modelId);
        if (model is null || model.Source != ModelSource.HuggingFace || string.IsNullOrWhiteSpace(model.RepoId))
        {
            return false;
        }

        var modelBasePath = GetModelBasePath(engineType);
        var localPath = Path.Combine(modelBasePath, modelId);

        // Get the latest remote timestamp from the HF API
        var remoteInfo = await hfClient.GetModelInfoAsync(model.RepoId, ct);
        var remoteLastModified = remoteInfo?.LastModified ?? model.LastModified;

        return await hfDownloader.CheckForUpdateAsync(model.RepoId, localPath, remoteLastModified, ct);
    }

    /// <summary>
    /// Updates a HuggingFace-sourced model by re-running the HF CLI download.
    /// </summary>
    public async Task<ModelDownloadResult> UpdateModelAsync(
        EngineType engineType,
        string modelId,
        IProgress<ModelDownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        var model = catalogService.GetModelById(engineType, modelId);
        if (model is null || model.Source != ModelSource.HuggingFace || string.IsNullOrWhiteSpace(model.RepoId))
        {
            return ModelDownloadResult.Failure(modelId, "Model is not a HuggingFace model or not found in catalog");
        }

        var modelBasePath = GetModelBasePath(engineType);
        var result = await hfDownloader.DownloadModelAsync(model.RepoId, modelBasePath, progress, ct);

        if (result.Success)
        {
            logger.LogInformation("Updated HuggingFace model {ModelId} at {Path}", modelId, result.LocalPath);
        }

        return result;
    }

    private string GetConfiguredActiveModel(EngineType engineType) =>
        engineType switch
        {
            EngineType.Stt => sttOptionsMonitor.CurrentValue.ActiveModel,
            EngineType.OfflineStt => ResolveOfflineSttActiveModel(),
            EngineType.Vad => vadOptionsMonitor.CurrentValue.ActiveModel,
            EngineType.WakeWord => wakeWordOptionsMonitor.CurrentValue.ActiveModel,
            EngineType.SpeakerEmbedding => diarizationOptionsMonitor.CurrentValue.ActiveModel,
            EngineType.SpeechEnhancement => enhancementOptionsMonitor.CurrentValue.ActiveModel,
            _ => throw new ArgumentOutOfRangeException(nameof(engineType)),
        };

    private string ResolveOfflineSttActiveModel()
    {
        var hybridPath = hybridSttOptionsMonitor.CurrentValue.ModelPath;
        if (!string.IsNullOrWhiteSpace(hybridPath) && Directory.Exists(hybridPath))
            return Path.GetFileName(hybridPath);

        // Fall back: scan the STT model base path for the best offline model
        var basePath = sttOptionsMonitor.CurrentValue.ModelBasePath;
        if (!Directory.Exists(basePath)) return "(not configured)";

        var bestOffline = Directory.EnumerateDirectories(basePath)
            .Where(d => Directory.EnumerateFiles(d, "tokens.txt", SearchOption.AllDirectories).Any())
            .Where(d => (Path.GetFileName(d) ?? "").Contains("parakeet", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(d => new DirectoryInfo(d).EnumerateFiles("*.onnx", SearchOption.AllDirectories)
                .Sum(static f => f.Length))
            .FirstOrDefault();

        return bestOffline is not null ? Path.GetFileName(bestOffline) : "(not configured)";
    }

    public string GetModelBasePath(EngineType engineType) =>
        engineType switch
        {
            EngineType.Stt => sttOptionsMonitor.CurrentValue.ModelBasePath,
            EngineType.OfflineStt => sttOptionsMonitor.CurrentValue.ModelBasePath,
            EngineType.Vad => vadOptionsMonitor.CurrentValue.ModelBasePath,
            EngineType.WakeWord => wakeWordOptionsMonitor.CurrentValue.ModelBasePath,
            EngineType.SpeakerEmbedding => diarizationOptionsMonitor.CurrentValue.ModelBasePath,
            EngineType.SpeechEnhancement => enhancementOptionsMonitor.CurrentValue.ModelBasePath,
            _ => throw new ArgumentOutOfRangeException(nameof(engineType)),
        };

    private static string GetSafeModelDirectory(string modelId, string basePath)
    {
        if (string.IsNullOrWhiteSpace(modelId)
            || modelId.Contains("..", StringComparison.Ordinal)
            || modelId.Contains(Path.DirectorySeparatorChar)
            || modelId.Contains(Path.AltDirectorySeparatorChar)
            || modelId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException($"Invalid model ID: '{modelId}'", nameof(modelId));
        }

        var fullBasePath = Path.GetFullPath(basePath);
        var normalizedBasePath = fullBasePath.EndsWith(Path.DirectorySeparatorChar)
            ? fullBasePath
            : $"{fullBasePath}{Path.DirectorySeparatorChar}";
        var modelDir = Path.GetFullPath(Path.Combine(fullBasePath, modelId));

        var pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!modelDir.StartsWith(normalizedBasePath, pathComparison))
        {
            throw new ArgumentException(
                $"Model ID '{modelId}' resolves outside model directory",
                nameof(modelId));
        }

        return modelDir;
    }

    private static bool IsUsableModelDirectory(string modelDirectory) =>
        Directory.Exists(modelDirectory)
        && Directory.EnumerateFiles(modelDirectory, "*.onnx", SearchOption.AllDirectories).Any();

    private static bool ContainsAnyFile(IEnumerable<string> files, params string[] expectedFiles) =>
        expectedFiles.All(expectedFile => ContainsFile(files, expectedFile));

    private static bool ContainsFile(IEnumerable<string> files, string expectedFile) =>
        files.Any(file => string.Equals(file, expectedFile, StringComparison.OrdinalIgnoreCase));
}
