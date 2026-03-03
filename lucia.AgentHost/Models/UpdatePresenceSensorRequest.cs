using lucia.Agents.Models.HomeAssistant;

namespace lucia.AgentHost.Models;

/// <summary>
/// Request body for updating a presence sensor mapping.
/// Only non-null fields are applied.
/// </summary>
public sealed class UpdatePresenceSensorRequest
{
    public string? AreaId { get; set; }
    public string? AreaName { get; set; }
    public PresenceConfidence? Confidence { get; set; }
    public bool? IsDisabled { get; set; }
}
