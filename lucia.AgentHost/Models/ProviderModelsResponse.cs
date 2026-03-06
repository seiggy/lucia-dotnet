namespace lucia.AgentHost.Models;

/// <summary>
/// Response for provider model enumeration endpoints.
/// </summary>
public sealed class ProviderModelsResponse
{
    public List<string> Models { get; set; } = [];
    public string? Error { get; set; }
}
