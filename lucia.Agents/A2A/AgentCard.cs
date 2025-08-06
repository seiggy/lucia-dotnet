namespace lucia.Agents.A2A;

public class AgentCard
{
    public string ProtocolVersion { get; set; } = "0.2.5";
    public required string Name { get; set; }
    public required string Description { get; set; }
    
    // Recommended: use the RFC8615 well-known URI: https://{server_domain}/.well-known/agent.json
    public required string Uri { get; set; }
    public string? PreferredTransport { get; set; } = "JSONRPC";
    public AgentInterface[]? AdditionalInterfaces { get; set; } = null;
    public string? IconUrl { get; set; }
    public AgentProvider? Provider { get; set; } = null;
    
    public string version { get; set; } = "1.0.0";
    public string? DocumentationUrl { get; set; }
    public required AgentCapabilities Capabilities { get; set; }
    
    public Dictionary<string, SecurityScheme>? SecuritySchemes { get; set; }
    
    public Dictionary<string, string>? Security { get; set; }
    public string[] DefaultInputModes { get; set; } = [];
    public string[] DefaultOutputModes { get; set; } = [];
    public bool? SupportsAuthenticatedExtendedCard { get; set; }
}