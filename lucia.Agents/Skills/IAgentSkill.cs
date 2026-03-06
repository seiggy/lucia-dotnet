namespace lucia.Agents.Skills;

internal interface IAgentSkill
{
    /// <summary>
    /// The Home Assistant entity domains this skill operates on (e.g. "light", "switch").
    /// Loaded from the skill's configuration section and hot-reloaded at runtime.
    /// </summary>
    IReadOnlyList<string> EntityDomains { get; }

    Task InitializeAsync(CancellationToken cancellationToken = default);
}
