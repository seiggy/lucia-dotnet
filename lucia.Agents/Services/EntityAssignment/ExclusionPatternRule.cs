using System.Text.RegularExpressions;

using lucia.Agents.Abstractions;
using lucia.Agents.Models.HomeAssistant;

namespace lucia.Agents.Services.EntityAssignment;

/// <summary>
/// Excludes infrastructure and diagnostic entities by matching entity_id patterns.
/// Examples: child locks, network equipment LEDs, satellite device settings, camera toggles.
/// </summary>
public sealed partial class ExclusionPatternRule : IEntityAssignmentRule
{
    public int Order => 100;

    private static readonly Regex[] s_exclusionPatterns =
    [
        ChildLockPattern(),
        LedSuffixPattern(),
        LedRingPattern(),
        InfrastructureLightPattern(),
        SatelliteSettingPattern(),
        ChimeExtenderPattern(),
        PrivacyModePattern(),
        DeterModePattern(),
        EmergencyHeatPattern(),
        UpsPattern(),
        PanelSoundPattern(),
        DisplayAutoOffPattern(),
    ];

    public bool TryEvaluate(
        HomeAssistantEntity entity,
        IReadOnlyDictionary<string, List<string>> domainAgentMap,
        out List<string>? assignedAgents)
    {
        foreach (var pattern in s_exclusionPatterns)
        {
            if (pattern.IsMatch(entity.EntityId))
            {
                assignedAgents = [];
                return true;
            }
        }

        assignedAgents = null;
        return false;
    }

    // Device config toggles (child_lock as suffix or mid-word)
    [GeneratedRegex(@"_child_lock(?:_\d+)?$|_child_lock_", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ChildLockPattern();

    // Network equipment LEDs (AP LEDs, switch LEDs, outlet LEDs)
    [GeneratedRegex(@"(?:^light\..+_led$)|(?:^light\.(?:usw_|.*_ap_|.*_ont_))", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex LedSuffixPattern();

    // Satellite LED rings
    [GeneratedRegex(@"_led_ring", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex LedRingPattern();

    // Infrastructure light entities (UniFi-style naming)
    [GeneratedRegex(@"^light\..*(?:usw[-_]|uap[-_]|udm[-_]|unvr[-_])", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex InfrastructureLightPattern();

    // Satellite device settings (wake, mute, firmware, bluetooth, tracking, snapcast)
    [GeneratedRegex(@"satellite\d*.*(?:_wake_sound|_mute|_beta_firmware|_bluetooth|_multi_target_tracking|_snapcast)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex SatelliteSettingPattern();

    // Camera/doorbell chime extender
    [GeneratedRegex(@"_chime_extender", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ChimeExtenderPattern();

    // Camera/doorbell privacy mode
    [GeneratedRegex(@"_privacy_mode", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex PrivacyModePattern();

    // Camera/doorbell deterrence mode
    [GeneratedRegex(@"_deter_mode", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex DeterModePattern();

    // Climate emergency heat switch
    [GeneratedRegex(@"_emergency_heat", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex EmergencyHeatPattern();

    // UPS power management
    [GeneratedRegex(@"_ups_", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex UpsPattern();

    // Appliance panel sound config
    [GeneratedRegex(@"_panel_sound", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex PanelSoundPattern();

    // Appliance display auto-off config
    [GeneratedRegex(@"_display_auto_off", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex DisplayAutoOffPattern();
}
