namespace lucia.Agents.Abstractions;

/// <summary>
/// Abstraction over the dynamic agent background service so API handlers can trigger
/// catalog rebuilds without depending on the sealed hosted-service implementation.
/// </summary>
public interface IDynamicAgentLoader
{
    /// <summary>Rebuilds all dynamic agents from their MongoDB definitions.</summary>
    Task ReloadAsync(CancellationToken ct = default);
}
