using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace lucia.Agents.Services;

/// <summary>
/// Reports unhealthy until <see cref="AgentInitializationService"/> has completed
/// registering all in-process agents. Tagged "ready" so it only affects the
/// readiness probe (<c>/health</c>), not the liveness probe (<c>/alive</c>).
/// </summary>
public sealed class AgentInitializationHealthCheck : IHealthCheck
{
    private readonly AgentInitializationStatus _status;

    public AgentInitializationHealthCheck(AgentInitializationStatus status)
    {
        _status = status;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (_status.IsReady)
            return Task.FromResult(HealthCheckResult.Healthy("Agent initialization complete."));

        // Pre-setup: the app is running fine, just waiting for the user to complete
        // the setup wizard. Report healthy so Kubernetes/Docker health checks pass
        // and traffic can reach the onboarding endpoints.
        if (_status.IsWaitingForConfig)
            return Task.FromResult(HealthCheckResult.Healthy("Waiting for setup wizard — agents not yet initialized."));

        // Post-config, pre-ready: agent initialization is actively running
        return Task.FromResult(HealthCheckResult.Degraded("Agent initialization in progress."));
    }
}
