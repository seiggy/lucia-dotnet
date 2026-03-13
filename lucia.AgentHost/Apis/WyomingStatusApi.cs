using lucia.Wyoming.Diarization;
using lucia.Wyoming.Stt;
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
        IWakeWordDetector? wakeWordDetector,
        IDiarizationEngine? diarizationEngine,
        CustomWakeWordManager? wakeWordManager)
    {
        return Results.Ok(new
        {
            Stt = new { Ready = sttEngine?.IsReady ?? false },
            WakeWord = new { Ready = wakeWordDetector?.IsReady ?? false },
            Diarization = new { Ready = diarizationEngine?.IsReady ?? false },
            CustomWakeWords = new { Ready = wakeWordManager?.IsReady ?? false },
            Configured = (sttEngine?.IsReady ?? false) || (wakeWordDetector?.IsReady ?? false),
        });
    }
}
