namespace lucia.Wyoming.Audio;

using lucia.Wyoming.Stt;
using lucia.Wyoming.Vad;

/// <summary>
/// Orchestrates audio processing: VAD segmentation → STT transcription.
/// </summary>
public sealed class AudioPipeline
{
    private readonly IVadEngine _vadEngine;
    private readonly ISttEngine _stt;

    public AudioPipeline(IVadEngine vadEngine, ISttEngine stt)
    {
        _vadEngine = vadEngine;
        _stt = stt;
    }

    // TODO: Implement ProcessAudioChunk, GetTranscript, etc. in integration phase
}
