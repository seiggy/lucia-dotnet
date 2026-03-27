using lucia.Agents.Extensions;
﻿using A2A;

namespace lucia.A2AHost.AgentRegistry;

public sealed class AgentRegistryClient(HttpClient httpClient, IConfiguration configuration, ILogger<AgentRegistryClient> logger)
{
    /// <summary>
    /// Configures the internal auth token on the HttpClient if available.
    /// Called once during registration to ensure token is applied.
    /// </summary>
    private void EnsureAuthToken()
    {
        if (httpClient.DefaultRequestHeaders.Authorization is not null)
            return;

        var token = configuration["InternalAuth:Token"];
        if (!string.IsNullOrWhiteSpace(token))
        {
            httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }
    }

    internal async Task RegisterAgentAsync(AgentCard hostedAgent, CancellationToken cancellationToken)
    {
        EnsureAuthToken();
        var agentUrl = hostedAgent.GetUrl() ?? hostedAgent.Name ?? "unknown";
        logger.LogInformation("Registering agent {AgentName} with URL {AgentUrl} at registry {RegistryBase}",
            hostedAgent.Name, agentUrl, httpClient.BaseAddress);
        var formData = new Dictionary<string, string>
        {
            ["agentId"] = agentUrl
        };
        var form = new FormUrlEncodedContent(formData);
        try
        {
            var response = await httpClient.PostAsync("/agents/register", form, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                logger.LogInformation("Agent {AgentName} registered successfully (HTTP {StatusCode})",
                    hostedAgent.Name, (int)response.StatusCode);
            }
            else
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogWarning("Agent registration returned {StatusCode} for {AgentUrl}. Response: {Body}",
                    (int)response.StatusCode, hostedAgent.GetUrl()!, body);
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error trying to register agent {AgentName} ({AgentUrl}) with the registry at {RegistryBase}",
                hostedAgent.Name, hostedAgent.GetUrl()!, httpClient.BaseAddress);
        }
    }

    /// <summary>
    /// Checks if an agent URL is present in the remote registry.
    /// </summary>
    internal async Task<bool> IsRegisteredAsync(string agentUrl, CancellationToken cancellationToken)
    {
        EnsureAuthToken();
        try
        {
            var response = await httpClient.GetAsync("/agents", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Registry returned {StatusCode} when checking agent registration",
                    (int)response.StatusCode);
                return false;
            }

            var agents = await response.Content.ReadFromJsonAsync<List<AgentCard>>(cancellationToken);
            return agents?.Any(a => string.Equals(a.GetUrl(), agentUrl, StringComparison.OrdinalIgnoreCase)) ?? false;
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Failed to check registration status with registry");
            return false;
        }
    }

    internal async Task UnregisterAgentAsync(AgentCard hostedAgent, CancellationToken cancellationToken)
    {
        EnsureAuthToken();
        var agentUrl = hostedAgent.GetUrl() ?? hostedAgent.Name ?? "unknown";
        logger.LogInformation("Unregistering agent {AgentName} with URL {AgentUrl}",
            hostedAgent.Name, agentUrl);
        try
        {
            var response = await httpClient.DeleteAsync($"/agents/{Uri.EscapeDataString(agentUrl)}", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Agent unregistration returned {StatusCode} for {AgentUrl}",
                    (int)response.StatusCode, agentUrl);
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error trying to unregister agent {AgentUrl} from the registry. Check if it's online?",
                agentUrl);
        }
    }
}
