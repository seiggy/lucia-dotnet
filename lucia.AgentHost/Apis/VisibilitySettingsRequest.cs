namespace lucia.AgentHost.Apis;

/// <summary>
/// Request body for updating visibility settings.
/// </summary>
public sealed class VisibilitySettingsRequest
{
    public bool UseExposedEntitiesOnly { get; set; }
}
