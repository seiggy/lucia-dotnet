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
        ISttEngine? sttEngine,
        IVadEngine? vadEngine,
        IWakeWordDetector? wakeWordDetector,
        IDiarizationEngine? diarizationEngine,
        ISpeechEnhancer? speechEnhancer,
        CustomWakeWordManager? wakeWordManager,
        ModelManager manager)
    {
        return Results.Ok(new
        {
            Stt = new
            {
                Ready = sttEngine?.IsReady ?? false,
                ActiveModel = manager.GetActiveModelId(EngineType.Stt),
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
            Configured = (sttEngine?.IsReady ?? false) || (wakeWordDetector?.IsReady ?? false),
        });
    }
}
