namespace lucia.Agents.Services;

/// <summary>
/// Signals whether agent initialization has completed.
/// Used by health checks to gate readiness until all agents are registered.
/// </summary>
public sealed class AgentInitializationStatus
{
    private volatile bool _isReady;

    public bool IsReady => _isReady;

    public void MarkReady() => _isReady = true;
}
