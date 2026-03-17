namespace lucia.Wyoming.Wyoming;

/// <summary>
/// Configuration options for the Wyoming voice protocol server.
/// </summary>
public sealed class WyomingOptions
{
    public const string SectionName = "Wyoming";

    /// <summary>TCP listen address.</summary>
    public string Host { get; set; } = "0.0.0.0";

    /// <summary>Wyoming TCP port.</summary>
    public int Port { get; set; } = 10400;

    /// <summary>Maximum concurrent always-on wake word streams.</summary>
    public int MaxWakeWordStreams { get; set; } = 30;

    /// <summary>Maximum concurrent STT sessions (burst).</summary>
    public int MaxConcurrentSttSessions { get; set; } = 4;

    /// <summary>Maximum concurrent TTS syntheses (burst).</summary>
    public int MaxConcurrentTtsSyntheses { get; set; } = 2;

    /// <summary>Maximum header line length in bytes (default 8KB).</summary>
    public int MaxHeaderLineBytes { get; set; } = 8192;

    /// <summary>Maximum extra data length in bytes (default 64KB).</summary>
    public int MaxDataLength { get; set; } = 65536;

    /// <summary>Maximum binary payload length in bytes (default 1MB — ~30s of 16kHz 16-bit mono audio).</summary>
    public int MaxPayloadLength { get; set; } = 1_048_576;

    /// <summary>Read timeout per event in seconds. Connections idle longer are closed. Default 60s.</summary>
    public int ReadTimeoutSeconds { get; set; } = 60;

    /// <summary>Zeroconf service name.</summary>
    public string ServiceName { get; set; } = "lucia-wyoming";

    /// <summary>Timeout for continue_conversation follow-up listening.</summary>
    public TimeSpan FollowUpTimeout { get; set; } = TimeSpan.FromSeconds(10);
}
