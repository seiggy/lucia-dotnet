namespace lucia.AgentHost.Apis;

/// <summary>
/// Agent info returned by the available-agents endpoint, including
/// the entity domains the agent's skills operate on.
/// </summary>
public sealed record AgentInfo
{
    public required string Name { get; init; }
    public required List<string> Domains { get; init; }
}
