using lucia.Agents.Abstractions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace lucia.A2AHost.AgentRegistry;

/// <summary>
/// Reports A2A host readiness and agent registration status as structured health data.
/// The host is considered healthy when its own services are operational. Registration
/// status is reported as supplemental data — a missing registration does not make the
/// host itself unhealthy, but is visible in the health response for diagnostics.
/// If agents are found unregistered (e.g. after a registry restart), it will
/// automatically attempt re-registration.
/// </summary>
public sealed class AgentRegistrationHealthCheck(
    AgentRegistryClient registryClient,
    IEnumerable<ILuciaAgent> agents,
    ILogger<AgentRegistrationHealthCheck> logger) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var agentList = agents.ToList();
        if (agentList.Count == 0)
        {
            return HealthCheckResult.Healthy("No agents hosted",
                new Dictionary<string, object> { ["agents"] = 0 });
        }

        var registrationData = new Dictionary<string, object>();
        var registered = new List<string>();
        var unregistered = new List<string>();

        foreach (var agent in agentList)
        {
            var card = agent.GetAgentCard();
            var name = card.Name ?? agent.GetType().Name;

            if (string.IsNullOrWhiteSpace(card.Url) || card.Url == "unknown")
            {
                unregistered.Add(name);
                registrationData[name] = "not_configured";
                continue;
            }

            try
            {
                var isRegistered = await registryClient.IsRegisteredAsync(card.Url, cancellationToken);

                if (!isRegistered)
                {
                    logger.LogWarning("Agent {AgentName} ({AgentUrl}) not found in registry, attempting re-registration",
                        name, card.Url);

                    await registryClient.RegisterAgentAsync(card, cancellationToken);
                    isRegistered = await registryClient.IsRegisteredAsync(card.Url, cancellationToken);

                    if (isRegistered)
                    {
                        logger.LogInformation("Agent {AgentName} successfully re-registered with registry", name);
                    }
                }

                if (isRegistered)
                {
                    registered.Add(name);
                    registrationData[name] = "registered";
                }
                else
                {
                    unregistered.Add(name);
                    registrationData[name] = "unregistered";
                }
            }
            catch (Exception ex)
            {
                unregistered.Add(name);
                registrationData[name] = $"error: {ex.Message}";
            }
        }

        var data = new Dictionary<string, object>
        {
            ["agents"] = agentList.Count,
            ["registered"] = registered.Count,
            ["unregistered"] = unregistered.Count,
            ["registration"] = registrationData
        };

        // Host is healthy as long as it's running — registration is reported
        // as supplemental data, not a readiness gate
        if (unregistered.Count > 0)
        {
            return HealthCheckResult.Degraded(
                $"{registered.Count}/{agentList.Count} agent(s) registered", data: data);
        }

        return HealthCheckResult.Healthy(
            $"All {agentList.Count} agent(s) registered", data: data);
    }
}
