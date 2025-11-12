using A2A;

namespace lucia.A2AHost.AgentRegistry
{
    public class AgentRegistryClient(HttpClient httpClient, ILogger<AgentRegistryClient> logger)
    {
        internal async Task RegisterAgentAsync(AgentCard hostedAgent, CancellationToken cancellationToken)
        {
            var form = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("agentId", hostedAgent.Url)
            });
            try
            {
                var response = await httpClient.PostAsync("/agents", form);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error trying to register the configured agent with the registry. Check if it's online?");
            }
        }

        internal async Task UnregisterAgentAsync(AgentCard hostedAgent, CancellationToken cancellationToken)
        {
            try
            {
                var response = await httpClient.DeleteAsync($"/agents/{Uri.EscapeDataString(hostedAgent.Url)}");
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error trying to register the configured agent with the registry. Check if it's online?");
            }

        }
    }
}
