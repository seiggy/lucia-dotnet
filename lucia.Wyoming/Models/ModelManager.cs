using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucia.Wyoming.Models;

public sealed class ModelManager(
    IOptionsMonitor<SttModelOptions> optionsMonitor,
    ModelCatalogService catalogService,
    ModelDownloader downloader,
    ILogger<ModelManager> logger)
{
    private string? _activeModelOverride;

    public string ActiveModelId => GetActiveModelId();

    public async Task<bool> ValidateActiveModelAsync(CancellationToken ct = default)
    {
        var activeModelId = GetActiveModelId();
        var modelDirectory = GetModelDirectory(activeModelId);

        if (IsUsableModelDirectory(modelDirectory))
        {
            return DetectModelArchitecture(modelDirectory) is not ModelArchitecture.Unknown;
        }

        var modelDefinition = catalogService.GetModelById(activeModelId);
        if (modelDefinition is null || !modelDefinition.IsDefault || !optionsMonitor.CurrentValue.AutoDownloadDefault)
        {
            logger.LogWarning("Active Wyoming model {ModelId} is not installed at {ModelDirectory}", activeModelId, modelDirectory);
            return false;
        }

        var result = await downloader
            .DownloadModelAsync(modelDefinition, optionsMonitor.CurrentValue.ModelBasePath, ct: ct)
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

    public async Task<bool> SwitchActiveModelAsync(string modelId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);

        var modelDirectory = GetModelDirectory(modelId);
        if (!IsUsableModelDirectory(modelDirectory))
        {
            var modelDefinition = catalogService.GetModelById(modelId);
            if (modelDefinition is null)
            {
                if (!optionsMonitor.CurrentValue.AllowCustomModels)
                {
                    throw new InvalidOperationException($"Custom Wyoming model '{modelId}' is not allowed.");
                }

                throw new DirectoryNotFoundException($"Wyoming model directory '{modelDirectory}' was not found.");
            }

            var downloadResult = await downloader
                .DownloadModelAsync(modelDefinition, optionsMonitor.CurrentValue.ModelBasePath, ct: ct)
                .ConfigureAwait(false);

            if (!downloadResult.Success)
            {
                throw new InvalidOperationException(downloadResult.Error ?? $"Failed to download Wyoming model '{modelId}'.");
            }
        }

        var architecture = DetectModelArchitecture(modelDirectory);
        if (architecture is ModelArchitecture.Unknown)
        {
            throw new InvalidOperationException($"Could not determine the architecture for Wyoming model '{modelId}'.");
        }

        _activeModelOverride = modelId;
        logger.LogInformation(
            "Switched active Wyoming model to {ModelId} with detected architecture {Architecture}",
            modelId,
            architecture);

        return true;
    }

    public Task DeleteModelAsync(string modelId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);

        var modelDirectory = GetModelDirectory(modelId);
        if (!Directory.Exists(modelDirectory))
        {
            return Task.CompletedTask;
        }

        Directory.Delete(modelDirectory, recursive: true);

        if (string.Equals(_activeModelOverride, modelId, StringComparison.Ordinal))
        {
            _activeModelOverride = null;
        }

        logger.LogInformation(
            "Deleted Wyoming model {ModelId} from {ModelDirectory}",
            modelId,
            modelDirectory);

        return Task.CompletedTask;
    }

    private string GetActiveModelId() =>
        string.IsNullOrWhiteSpace(_activeModelOverride)
            ? optionsMonitor.CurrentValue.ActiveModel
            : _activeModelOverride;

    private string GetModelDirectory(string modelId) =>
        Path.Combine(optionsMonitor.CurrentValue.ModelBasePath, modelId);

    private static bool IsUsableModelDirectory(string modelDirectory) =>
        Directory.Exists(modelDirectory)
        && Directory.EnumerateFiles(modelDirectory, "*.onnx", SearchOption.AllDirectories).Any();

    private static bool ContainsAnyFile(IEnumerable<string> files, params string[] expectedFiles) =>
        expectedFiles.All(expectedFile => ContainsFile(files, expectedFile));

    private static bool ContainsFile(IEnumerable<string> files, string expectedFile) =>
        files.Any(file => string.Equals(file, expectedFile, StringComparison.OrdinalIgnoreCase));
}
