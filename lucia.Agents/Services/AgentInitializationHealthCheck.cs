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
        return Task.FromResult(_status.IsReady
            ? HealthCheckResult.Healthy("Agent initialization complete.")
            : HealthCheckResult.Unhealthy("Agent initialization in progress."));
    }
}
