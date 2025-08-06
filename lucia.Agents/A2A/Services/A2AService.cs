using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.A2A.Services;

public class A2AService : IA2AService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<A2AService> _logger;
    private readonly ActivitySource _activitySource;
    
    public A2AService(
        HttpClient httpClient,
        ILogger<A2AService> logger,
        ActivitySource activitySource) 
    {
        _httpClient = httpClient;
        _logger = logger;
        _activitySource = activitySource;
    }
    
    public async Task<AgentCard> DownloadAgentCardAsync(string agentUri, CancellationToken cancellationToken = default)
    {
        var activity = _activitySource.StartActivity("DownloadAgentCard", ActivityKind.Client, agentUri);
        try
        {
            var agentCardJson = await _httpClient.GetStringAsync(agentUri, cancellationToken).ConfigureAwait(false);
            var agentCard = System.Text.Json.JsonSerializer.Deserialize<AgentCard>(agentCardJson);
            if (agentCard is null)
            {
                _logger.LogError("Failed to deserialize agent card from {AgentUri}", agentUri);
                _logger.LogTrace("Agent card JSON: {AgentCardJson}", agentCardJson);
                activity?.SetStatus(ActivityStatusCode.Error, "Deserialization failed");
                activity?.Stop();
                throw new InvalidOperationException($"Failed to deserialize agent card from {agentUri}");
            }
            _logger.LogTrace("Successfully downloaded agent card from {AgentUri}", agentUri);
            activity?.SetStatus(ActivityStatusCode.Ok, "Agent Download successful");
            activity?.Stop();
            return agentCard;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to build agent card from {AgentUri}", agentUri);
            activity?.SetStatus(ActivityStatusCode.Error, "Agent Download failed");
            activity?.Stop();
            throw;
        }
    }
}