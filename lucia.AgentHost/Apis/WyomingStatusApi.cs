using lucia.Wyoming.Audio;
using lucia.Wyoming.Diarization;
using lucia.Wyoming.Models;
using lucia.Wyoming.Stt;
using lucia.Wyoming.Vad;
using lucia.Wyoming.WakeWord;

namespace lucia.AgentHost.Apis;

public static class WyomingStatusApi
{
    public static void MapWyomingStatusEndpoints(this WebApplication app)
    {
        app.MapGet("/api/wyoming/status", GetWyomingStatus)
            .WithTags("Wyoming Status");
    }

    public static IResult GetWyomingStatus(
        IEnumerable<ISttEngine> sttEngines,
        IVadEngine? vadEngine,
        IWakeWordDetector? wakeWordDetector,
        IDiarizationEngine? diarizationEngine,
        ISpeechEnhancer? speechEnhancer,
        CustomWakeWordManager? wakeWordManager,
        OnnxProviderDetector providerDetector,
        ModelManager manager)
    {
        // Pick the engine matching the user's preferred STT engine type
        var engines = sttEngines.ToArray();
        var preferStreaming = manager.PreferredSttEngineType == EngineType.Stt;
        var activeEngine = preferStreaming
            ? (engines.OfType<SherpaSttEngine>().FirstOrDefault(static e => e.IsReady) as ISttEngine
                ?? engines.FirstOrDefault(static e => e.IsReady))
            : (engines.OfType<HybridSttEngine>().FirstOrDefault(static e => e.IsReady) as ISttEngine
                ?? engines.FirstOrDefault(static e => e.IsReady));
        activeEngine ??= engines.FirstOrDefault();

        var engineType = activeEngine switch
        {
            HybridSttEngine => "Hybrid (Offline Re-transcription)",
            SherpaSttEngine => "Sherpa Streaming",
            _ => activeEngine?.GetType().Name ?? "None",
        };

        // Resolve the active model ID based on the selected engine
        var activeModelId = activeEngine is HybridSttEngine
            ? manager.GetActiveModelId(EngineType.OfflineStt)
            : manager.GetActiveModelId(EngineType.Stt);

        return Results.Ok(new
        {
            Stt = new
            {
                Ready = activeEngine?.IsReady ?? false,
                ActiveModel = activeModelId,
                Engine = engineType,
            },
            Vad = new
            {
                Ready = vadEngine?.IsReady ?? false,
                ActiveModel = manager.GetActiveModelId(EngineType.Vad),
            },
            WakeWord = new
            {
                Ready = wakeWordDetector?.IsReady ?? false,
                ActiveModel = manager.GetActiveModelId(EngineType.WakeWord),
            },
            Diarization = new
            {
                Ready = diarizationEngine?.IsReady ?? false,
                ActiveModel = manager.GetActiveModelId(EngineType.SpeakerEmbedding),
            },
            SpeechEnhancement = new
            {
                Ready = speechEnhancer?.IsReady ?? false,
                ActiveModel = manager.GetActiveModelId(EngineType.SpeechEnhancement),
            },
            CustomWakeWords = new { Ready = wakeWordManager?.IsReady ?? false },
            OnnxProvider = new
            {
                Selected = providerDetector.BestProvider,
                SherpaProvider = providerDetector.BestSherpaProvider,
                providerDetector.IsAccelerated,
                Available = providerDetector.AvailableProviders,
            },
            Configured = (activeEngine?.IsReady ?? false) || (wakeWordDetector?.IsReady ?? false),
        });
    }
}
