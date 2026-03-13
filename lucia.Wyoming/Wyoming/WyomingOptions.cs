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

    /// <summary>Zeroconf service name.</summary>
    public string ServiceName { get; set; } = "lucia-wyoming";

    /// <summary>Timeout for continue_conversation follow-up listening.</summary>
    public TimeSpan FollowUpTimeout { get; set; } = TimeSpan.FromSeconds(10);
}
