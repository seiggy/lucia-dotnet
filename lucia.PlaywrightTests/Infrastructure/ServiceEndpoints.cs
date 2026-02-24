using Microsoft.Playwright;

namespace lucia.PlaywrightTests.Infrastructure;

/// <summary>
/// Discovers the lucia-dashboard URL from the running Aspire AppHost.
/// Falls back to environment variable LUCIA_DASHBOARD_URL if Aspire is not available.
/// </summary>
public static class ServiceEndpoints
{
    private static string? _dashboardUrl;
    private static string? _agentHostUrl;

    /// <summary>
    /// Returns the lucia-dashboard base URL.
    /// Priority: LUCIA_DASHBOARD_URL env var → default localhost:41221.
    /// </summary>
    public static string DashboardUrl =>
        _dashboardUrl ??= Environment.GetEnvironmentVariable("LUCIA_DASHBOARD_URL")
                          ?? "http://localhost:41221";

    /// <summary>
    /// Returns the lucia-agenthost base URL.
    /// Priority: LUCIA_AGENTHOST_URL env var → default https://localhost:7235.
    /// </summary>
    public static string AgentHostUrl =>
        _agentHostUrl ??= Environment.GetEnvironmentVariable("LUCIA_AGENTHOST_URL")
                          ?? "https://localhost:7235";

    public static void Override(string? dashboardUrl = null, string? agentHostUrl = null)
    {
        if (dashboardUrl is not null) _dashboardUrl = dashboardUrl;
        if (agentHostUrl is not null) _agentHostUrl = agentHostUrl;
    }
}
