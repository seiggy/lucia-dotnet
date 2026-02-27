namespace lucia.Agents.Services;

/// <summary>
/// Signals the current agent initialization phase.
/// Used by health checks to differentiate between "waiting for user configuration"
/// (healthy — app is up, setup wizard is accessible) and "initialization failed"
/// (unhealthy — an error occurred during agent registration).
/// </summary>
public sealed class AgentInitializationStatus
{
    private volatile bool _isReady;
    private volatile bool _isWaitingForConfig = true;

    /// <summary>All agents registered and ready to serve requests.</summary>
    public bool IsReady => _isReady;

    /// <summary>
    /// The service is waiting for user configuration (setup wizard).
    /// This is a normal pre-setup state and should NOT fail health checks,
    /// otherwise Kubernetes will never route traffic to the pod for onboarding.
    /// </summary>
    public bool IsWaitingForConfig => _isWaitingForConfig;

    public void MarkReady() => _isReady = true;

    public void MarkConfigurationReceived() => _isWaitingForConfig = false;
}
