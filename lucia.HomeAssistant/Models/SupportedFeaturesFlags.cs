namespace lucia.HomeAssistant.Models;

/// <summary>
/// Bitmask flags for Home Assistant entity <c>supported_features</c> attribute.
/// Values are domain-specific — each HA entity domain defines its own flag meanings.
/// Use <see cref="HasFlag"/> for proper bitmask checks.
/// </summary>
[Flags]
public enum SupportedFeaturesFlags
{
    None = 0,

    // assist_satellite flags (AssistSatelliteEntityFeature)
    Announce = 1,
    StartConversation = 2
}
