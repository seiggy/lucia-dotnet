using A2A;

namespace lucia.Agents.Services;

/// <summary>
/// Service for interacting with A2A protocol compliant agents
/// </summary>
public interface IA2AClientService
{
    /// <summary>
    /// Download an agent card from a given URI
    /// </summary>
    Task<AgentCard?> DownloadAgentCardAsync(string agentUri, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Send a JSON-RPC 2.0 message to an agent
    /// </summary>
    Task<JsonRpcResponse?> SendMessageAsync(string agentUrl, JsonRpcRequest request, CancellationToken cancellationToken = default);
}