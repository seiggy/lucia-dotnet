using lucia.Wyoming.Stt;
using lucia.Wyoming.WakeWord;
using Microsoft.Extensions.Options;

namespace lucia.Wyoming.Wyoming;

public sealed class WyomingServiceInfo
{
    private readonly ISttEngine? _sttEngine;
    private readonly IWakeWordDetector? _wakeWordDetector;

    public WyomingServiceInfo(
        IOptions<WyomingOptions> options,
        ISttEngine? sttEngine = null,
        IWakeWordDetector? wakeWordDetector = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _ = options.Value;
        _sttEngine = sttEngine;
        _wakeWordDetector = wakeWordDetector;
    }

    public InfoEvent BuildInfoEvent()
    {
        var sherpaAttribution = new Attribution
        {
            Name = "k2-fsa",
            Url = "https://github.com/k2-fsa/sherpa-onnx",
        };

        return new InfoEvent
        {
            Asr = IsSttReady(_sttEngine)
                ? [new AsrInfo
                {
                    Name = "sherpa-onnx",
                    Description = "Sherpa-ONNX Streaming ASR",
                    Version = "1.11.1",
                    Languages = ["en"],
                    Installed = true,
                    Attribution = sherpaAttribution,
                    Models =
                    [
                        new AsrModelInfo
                        {
                            Name = "sherpa-onnx-streaming",
                            Description = "Streaming speech recognition",
                            Version = "1.11.1",
                            Languages = ["en"],
                            Installed = true,
                            Attribution = sherpaAttribution,
                        },
                    ],
                }]
                : [],
            Tts = [],
            Wake = _wakeWordDetector?.IsReady == true
                ? [new WakeInfo
                {
                    Name = "sherpa-kws",
                    Description = "Sherpa-ONNX Keyword Spotter",
                    Version = "1.11.1",
                    Languages = ["en"],
                    Installed = true,
                    Attribution = sherpaAttribution,
                    Models =
                    [
                        new WakeModelInfo
                        {
                            Name = "sherpa-kws-zipformer",
                            Description = "Open-vocabulary keyword spotting",
                            Version = "1.11.1",
                            Languages = ["en"],
                            Installed = true,
                            Attribution = sherpaAttribution,
                        },
                    ],
                }]
                : [],
            Version = "1.0.0",
        };
    }

    private static bool IsSttReady(ISttEngine? sttEngine)
    {
        if (sttEngine is null)
        {
            return false;
        }

        var isReadyProperty = sttEngine.GetType().GetProperty("IsReady");
        return isReadyProperty?.PropertyType == typeof(bool)
            && isReadyProperty.GetValue(sttEngine) is true;
    }
}
