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
/// Runs a warm-up inference on each engine to eliminate first-request latency.
/// </summary>
public sealed class ModelStartupValidator(
    ModelManager modelManager,
    IEnumerable<ISttEngine> sttEngines,
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
        _ = (sttEngines, vadEngine, wakeWordDetector, diarizationEngine, speechEnhancer);

        await ActivateEngineAsync(EngineType.SpeechEnhancement, stoppingToken).ConfigureAwait(false);
        await ActivateEngineAsync(EngineType.Stt, stoppingToken).ConfigureAwait(false);
        await ActivateEngineAsync(EngineType.Vad, stoppingToken).ConfigureAwait(false);
        await ActivateEngineAsync(EngineType.WakeWord, stoppingToken).ConfigureAwait(false);
        await ActivateEngineAsync(EngineType.SpeakerEmbedding, stoppingToken).ConfigureAwait(false);

        // Warm up all STT engines with a dummy inference to eliminate first-request latency
        await WarmUpSttEnginesAsync(stoppingToken).ConfigureAwait(false);
    }

    private async Task WarmUpSttEnginesAsync(CancellationToken ct)
    {
        // 0.5s of silence at 16kHz — enough to warm up ONNX Runtime internals
        var dummyAudio = new float[8000];

        foreach (var engine in sttEngines)
        {
            if (!engine.IsReady) continue;

            var engineName = engine.GetType().Name;
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();

                using var session = engine.CreateSession();
                session.AcceptAudioChunk(dummyAudio, 16000);
                _ = session.GetFinalResult();

                sw.Stop();
                logger.LogInformation(
                    "Warm-up inference for {Engine} completed in {ElapsedMs}ms",
                    engineName, sw.ElapsedMilliseconds);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Warm-up inference failed for {Engine}", engineName);
            }
        }

        // Warm up diarization embedding extraction
        if (diarizationEngine.IsReady)
        {
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                _ = diarizationEngine.ExtractEmbedding(dummyAudio, 16000);
                sw.Stop();
                logger.LogInformation(
                    "Warm-up inference for diarization completed in {ElapsedMs}ms",
                    sw.ElapsedMilliseconds);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Warm-up inference failed for diarization");
            }
        }

        // Warm up speech enhancement
        if (speechEnhancer.IsReady)
        {
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                using var enhSession = speechEnhancer.CreateSession();
                _ = enhSession.Process(dummyAudio);
                sw.Stop();
                logger.LogInformation(
                    "Warm-up inference for speech enhancement completed in {ElapsedMs}ms",
                    sw.ElapsedMilliseconds);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Warm-up inference failed for speech enhancement");
            }
        }

        logger.LogInformation("All voice pipeline warm-up complete");
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
