using lucia.Agents.Models;

namespace lucia.AgentHost.Extensions;

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
