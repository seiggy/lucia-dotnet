namespace lucia.Agents.Skills;

/// <summary>
/// Configurable options for <see cref="SecurityControlSkill"/>.
/// </summary>
public sealed class SecurityControlSkillOptions
{
    /// <summary>
    /// Configuration section name for security skill options.
    /// </summary>
    public const string SectionName = "SecurityControlSkill";

    /// <summary>
    /// Default alarm code used when the caller does not provide one.
    /// </summary>
    public string? DefaultAlarmCode { get; set; }

    /// <summary>
    /// Enables camera entities in security status responses.
    /// </summary>
    public bool EnableCameraSnapshot { get; set; }

    /// <summary>
    /// Home Assistant domains this skill may inspect or control.
    /// </summary>
    public List<string> AllowedDomains { get; set; } = ["alarm_control_panel", "lock", "camera"];
}
