namespace lucia.AgentHost.Extensions;

/// <summary>
/// Request body for adding a new plugin repository.
/// </summary>
public sealed class AddPluginRepositoryRequest
{
    public required string Url { get; set; }
    public string? Branch { get; set; }
    public string? ManifestPath { get; set; }
}
