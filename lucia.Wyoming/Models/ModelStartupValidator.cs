using lucia.Wyoming.Audio;
using lucia.Wyoming.Diarization;
using lucia.Wyoming.Stt;
using lucia.Wyoming.Vad;
using lucia.Wyoming.WakeWord;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace lucia.Wyoming.Models;

/// <summary>
/// Ensures all active engine models are downloaded and loaded before the Wyoming server
/// starts accepting connections. Constructor-injects every engine singleton to force
/// construction (and subscription to model-change events) before activation events fire.
/// </summary>
public sealed class ModelStartupValidator(
    ModelManager modelManager,
    ISttEngine sttEngine,
    IVadEngine vadEngine,
    IWakeWordDetector wakeWordDetector,
    IDiarizationEngine diarizationEngine,
    ISpeechEnhancer speechEnhancer,
    ILogger<ModelStartupValidator> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Engine singletons are resolved via constructor injection to ensure they are
        // constructed and subscribed to ActiveModelChanged before we fire events.
        _ = (sttEngine, vadEngine, wakeWordDetector, diarizationEngine, speechEnhancer);

        await ActivateEngineAsync(EngineType.SpeechEnhancement, stoppingToken).ConfigureAwait(false);
        await ActivateEngineAsync(EngineType.Stt, stoppingToken).ConfigureAwait(false);
        await ActivateEngineAsync(EngineType.Vad, stoppingToken).ConfigureAwait(false);
        await ActivateEngineAsync(EngineType.WakeWord, stoppingToken).ConfigureAwait(false);
        await ActivateEngineAsync(EngineType.SpeakerEmbedding, stoppingToken).ConfigureAwait(false);
    }

    private async Task ActivateEngineAsync(EngineType engineType, CancellationToken ct)
    {
        try
        {
            var activeModelId = modelManager.GetActiveModelId(engineType);
            logger.LogInformation("Validating active Wyoming {EngineType} model {ModelId} on startup", engineType, activeModelId);

            await modelManager.SwitchActiveModelAsync(engineType, activeModelId, ct).ConfigureAwait(false);

            logger.LogInformation("Wyoming {EngineType} model {ModelId} is ready", engineType, activeModelId);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Failed to validate Wyoming {EngineType} model on startup — engine will remain unavailable until manually configured", engineType);
        }
    }
}
