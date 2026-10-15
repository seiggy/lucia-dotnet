namespace lucia.Agents.Skills;

/// <summary>
/// A Lucia skill: a Home Assistant tool set hosted by a Lucia agent. Not to be confused with
/// <c>A2A.AgentSkill</c> (agent card metadata) or <c>Microsoft.Agents.AI.AgentSkill</c> (Agent Skills spec).
/// </summary>
internal interface ILuciaSkill
{
    /// <summary>
    /// The Home Assistant entity domains this skill operates on (e.g. "light", "switch").
    /// Loaded from the skill's configuration section and hot-reloaded at runtime.
    /// </summary>
    IReadOnlyList<string> EntityDomains { get; }

    Task InitializeAsync(CancellationToken cancellationToken = default);
}
