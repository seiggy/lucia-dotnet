namespace lucia.Agents.Abstractions;

/// <summary>
/// Describes a skill's configuration section for dynamic form generation.
/// The <see cref="OptionsType"/> is used via reflection to discover properties,
/// their types, and default values for the Definitions page editor.
/// </summary>
public sealed record SkillConfigSection
{
    /// <summary>
    /// The MongoDB configuration section name (e.g. "LightControlSkill").
    /// Must match the <c>SectionName</c> const on the options class.
    /// </summary>
    public required string SectionName { get; init; }

    /// <summary>
    /// Human-readable name shown in the UI (e.g. "Light Control").
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// The options class type (e.g. <c>typeof(LightControlSkillOptions)</c>).
    /// Properties are reflected to generate the editor schema.
    /// </summary>
    public required Type OptionsType { get; init; }
}
