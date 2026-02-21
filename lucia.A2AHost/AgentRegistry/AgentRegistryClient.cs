using A2A;

namespace lucia.A2AHost.AgentRegistry
{
    public sealed class AgentRegistryClient(HttpClient httpClient, ILogger<AgentRegistryClient> logger)
    {
        internal async Task RegisterAgentAsync(AgentCard hostedAgent, CancellationToken cancellationToken)
        {
            var formData = new Dictionary<string, string>
            {
                ["agentId"] = hostedAgent.Url
            };
            var form = new FormUrlEncodedContent(formData);
            try
            {
                var response = await httpClient.PostAsync("/agents/register", form, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning("Agent registration returned {StatusCode} for {AgentUrl}", (int)response.StatusCode, hostedAgent.Url);
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error trying to register agent {AgentUrl} with the registry. Check if it's online?", hostedAgent.Url);
            }
        }

        internal async Task UnregisterAgentAsync(AgentCard hostedAgent, CancellationToken cancellationToken)
        {
            try
            {
                var response = await httpClient.DeleteAsync($"/agents/{Uri.EscapeDataString(hostedAgent.Url)}", cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning("Agent unregistration returned {StatusCode} for {AgentUrl}", (int)response.StatusCode, hostedAgent.Url);
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error trying to unregister agent {AgentUrl} from the registry. Check if it's online?", hostedAgent.Url);
            }

        }
    }
}
