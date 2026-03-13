using Microsoft.Extensions.Options;

namespace lucia.Wyoming.Wyoming;

public sealed class WyomingServiceInfo
{
    private readonly WyomingOptions _options;

    public WyomingServiceInfo(IOptions<WyomingOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
    }

    public InfoEvent BuildInfoEvent()
    {
        var version = typeof(WyomingServiceInfo).Assembly.GetName().Version?.ToString();

        return new InfoEvent
        {
            Asr = [],
            Tts = [],
            Wake = [],
            Version = version,
        };
    }
}
