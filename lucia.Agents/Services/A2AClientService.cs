using A2A;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json;

namespace lucia.Agents.Services;

/// <summary>
/// Implementation of the A2A client service using the official A2A protocol v0.3.0.
/// TODO: Add helper methods to build JSON-RPC 2.0 compliant requests (id generation, params envelope)
/// and potentially streaming support once server-side is implemented.
/// </summary>
public class A2AClientService : IA2AClientService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<A2AClientService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public A2AClientService(HttpClient httpClient, ILogger<A2AClientService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
    }

    public async Task<AgentCard?> DownloadAgentCardAsync(string agentUri, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Downloading agent card from {AgentUri}", agentUri);

            // According to A2A v0.3.0 spec, the agent card should be available at the root URL
            var response = await _httpClient.GetAsync(agentUri, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to download agent card from {AgentUri}: {StatusCode}", agentUri, response.StatusCode);
                return null;
            }

            var agentCard = await response.Content.ReadFromJsonAsync<AgentCard>(_jsonOptions, cancellationToken);

            // Validate required fields per A2A v0.3.0 spec
            if (agentCard != null)
            {
                // Set protocol version if not provided
                if (string.IsNullOrEmpty(agentCard.ProtocolVersion))
                {
                    agentCard.ProtocolVersion = "0.3.0";
                }

                // Ensure URL is set
                if (string.IsNullOrEmpty(agentCard.Url))
                {
                    agentCard.Url = agentUri;
                }

                _logger.LogInformation("Successfully downloaded agent card for {AgentName} from {AgentUri}",
                    agentCard.Name, agentUri);
            }

            return agentCard;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading agent card from {AgentUri}", agentUri);
            return null;
        }
    }

    public async Task<JsonRpcResponse?> SendMessageAsync(string agentUrl, JsonRpcRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Sending JSON-RPC request to {AgentUrl}: {Method}", agentUrl, request.Method);

            var response = await _httpClient.PostAsJsonAsync(agentUrl, request, _jsonOptions, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to send message to {AgentUrl}: {StatusCode}", agentUrl, response.StatusCode);
                return null;
            }

            var jsonRpcResponse = await response.Content.ReadFromJsonAsync<JsonRpcResponse>(_jsonOptions, cancellationToken);

            if (jsonRpcResponse?.Error != null)
            {
                _logger.LogWarning("JSON-RPC error from {AgentUrl}: {ErrorCode} - {ErrorMessage}",
                    agentUrl, jsonRpcResponse.Error.Code, jsonRpcResponse.Error.Message);
            }

            return jsonRpcResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending message to {AgentUrl}", agentUrl);
            return null;
        }
    }
}
