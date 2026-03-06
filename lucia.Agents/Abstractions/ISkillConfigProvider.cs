namespace lucia.Agents.Abstractions;

/// <summary>
/// Implemented by agents that own skills with configurable options.
/// Exposes the skill config sections so the Definitions page can render
/// a dynamic editor and write values to the config store.
/// </summary>
public interface ISkillConfigProvider
{
    /// <summary>
    /// Returns the skill configuration sections this agent owns.
    /// Each section maps to an <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}"/>
    /// that hot-reloads from the MongoDB configuration store.
    /// </summary>
    IReadOnlyList<SkillConfigSection> GetSkillConfigSections();
}
