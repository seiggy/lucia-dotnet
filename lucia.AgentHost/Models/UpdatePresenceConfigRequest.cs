namespace lucia.AgentHost.Models;

/// <summary>
/// Request body for updating presence detection global configuration.
/// </summary>
public sealed class UpdatePresenceConfigRequest
{
    public bool? Enabled { get; set; }
}
