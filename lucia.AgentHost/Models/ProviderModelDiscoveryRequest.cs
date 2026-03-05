using lucia.Agents.Configuration;
using lucia.Agents.Configuration.UserConfiguration;

namespace lucia.AgentHost.Models;

/// <summary>
/// Request body for model discovery without persisting a provider configuration.
/// </summary>
public sealed class ProviderModelDiscoveryRequest
{
    public ProviderType ProviderType { get; set; }
    public string? Endpoint { get; set; }
    public ModelAuthConfig Auth { get; set; } = new();
}
