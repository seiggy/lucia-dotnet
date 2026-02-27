using lucia.Agents.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.Extensions;

/// <summary>
/// Seeds setup wizard values from environment variables for headless/Docker deployments.
/// When DASHBOARD_API_KEY, HomeAssistant__BaseUrl, HomeAssistant__AccessToken, and optionally
/// LUCIA_HA_API_KEY are set in .env, Lucia auto-configures and skips the setup wizard.
/// </summary>
public static class SetupSeedExtensions
{
    /// <summary>
    /// Seeds API keys and HA config from environment. If all required env vars are present
    /// and seeding succeeds, marks Auth:SetupComplete so the wizard is skipped.
    /// </summary>
    public static async Task SeedSetupFromEnvAsync(
        this IApiKeyService apiKeyService,
        ConfigStoreWriter configStore,
        IConfiguration configuration,
        ILogger logger,
        CancellationToken ct = default)
    {
        var dashboardKey = configuration["DASHBOARD_API_KEY"]?.Trim();
        var haUrl = configuration["HomeAssistant__BaseUrl"]?.Trim();
        var haToken = configuration["HomeAssistant__AccessToken"]?.Trim();
        var luciaHaKey = configuration["LUCIA_HA_API_KEY"]?.Trim();

        var seededKeys = 0;
        var seededHa = false;

        if (!string.IsNullOrEmpty(dashboardKey))
        {
            var result = await apiKeyService.CreateKeyFromPlaintextAsync("Dashboard", dashboardKey, ct).ConfigureAwait(false);
            if (result is not null)
            {
                seededKeys++;
                logger.LogInformation("Seeded Dashboard API key from DASHBOARD_API_KEY");
            }
        }

        if (!string.IsNullOrEmpty(luciaHaKey))
        {
            var result = await apiKeyService.CreateKeyFromPlaintextAsync("Home Assistant", luciaHaKey, ct).ConfigureAwait(false);
            if (result is not null)
            {
                seededKeys++;
                logger.LogInformation("Seeded Home Assistant API key from LUCIA_HA_API_KEY");
            }
        }

        if (!string.IsNullOrEmpty(haUrl) && !string.IsNullOrEmpty(haToken))
        {
            var existingUrl = await configStore.GetAsync("HomeAssistant:BaseUrl", ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(existingUrl))
            {
                await configStore.SetAsync("HomeAssistant:BaseUrl", haUrl.TrimEnd('/'), "env-seed", cancellationToken: ct).ConfigureAwait(false);
                await configStore.SetAsync("HomeAssistant:AccessToken", haToken, "env-seed", isSensitive: true, cancellationToken: ct).ConfigureAwait(false);
                seededHa = true;
                logger.LogInformation("Seeded Home Assistant connection from env (BaseUrl, AccessToken)");
            }
        }

        if (seededKeys > 0 || seededHa)
        {
            var hasAnyKeys = await apiKeyService.HasAnyKeysAsync(ct).ConfigureAwait(false);
            var hasHaConnection = !string.IsNullOrEmpty(haUrl) && !string.IsNullOrEmpty(haToken);

            if (hasAnyKeys && (!string.IsNullOrEmpty(dashboardKey) || hasHaConnection))
            {
                var existingComplete = await configStore.GetAsync("Auth:SetupComplete", ct).ConfigureAwait(false);
                if (!string.Equals(existingComplete, "true", StringComparison.OrdinalIgnoreCase))
                {
                    await configStore.SetAsync("Auth:SetupComplete", "true", "env-seed", cancellationToken: ct).ConfigureAwait(false);
                    logger.LogInformation("Headless setup complete from env — wizard skipped. Use DASHBOARD_API_KEY to log in.");
                }
            }
        }
    }
}
