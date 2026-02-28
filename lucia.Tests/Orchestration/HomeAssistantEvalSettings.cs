namespace lucia.Tests.Orchestration;

/// <summary>
/// Home Assistant connection settings for eval tests.
/// Maps to <c>EvalConfiguration:HomeAssistant</c> in <c>appsettings.json</c>.
/// Environment variable overrides:
/// <c>EvalConfiguration__HomeAssistant__BaseUrl</c>,
/// <c>EvalConfiguration__HomeAssistant__AccessToken</c>.
/// </summary>
public sealed class HomeAssistantEvalSettings
{
    /// <summary>
    /// Base URL of the Home Assistant instance (e.g. <c>http://homeassistant.local:8123</c>).
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Long-lived access token for the Home Assistant REST API.
    /// </summary>
    public string AccessToken { get; set; } = string.Empty;
}
