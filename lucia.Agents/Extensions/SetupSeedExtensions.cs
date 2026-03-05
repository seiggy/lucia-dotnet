using System.Security.Cryptography;
using lucia.Agents.Abstractions;
using lucia.Agents.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.Extensions;

/// <summary>
/// Seeds setup wizard values from environment variables for headless/Docker deployments.
/// When DASHBOARD_API_KEY, HomeAssistant__BaseUrl, HomeAssistant__AccessToken, and optionally
/// LUCIA_HA_API_KEY are set in .env, Lucia auto-configures and skips the setup wizard.
/// Optionally MusicAssistant__IntegrationId seeds the HA Music Assistant config entry ID for the music agent.
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
                logger.LogInformation("Seeded Home Assistant connection from env (BaseUrl, AccessToken)");
            }
        }

        // Music Assistant: seed HA integration config entry ID for headless (music agent uses this when calling HA music_assistant services)
        var musicAssistantEntryId = configuration["MusicAssistant__IntegrationId"]?.Trim();
        if (!string.IsNullOrEmpty(musicAssistantEntryId))
        {
            await configStore.SetAsync("MusicAssistant:IntegrationId", musicAssistantEntryId, "env-seed", cancellationToken: ct).ConfigureAwait(false);
            logger.LogInformation("Seeded Music Assistant integration from env (IntegrationId={EntryId})", musicAssistantEntryId);
        }

        // Skip wizard when all non-optional values are present (from env seed or existing store)
        var existingComplete = await configStore.GetAsync("Auth:SetupComplete", ct).ConfigureAwait(false);
        if (string.Equals(existingComplete, "true", StringComparison.OrdinalIgnoreCase))
            return;

        var keys = await apiKeyService.ListKeysAsync(ct).ConfigureAwait(false);
        var now = DateTime.UtcNow;
        var hasDashboardKey = keys.Any(
            k => string.Equals(k.Name, "Dashboard", StringComparison.Ordinal)
                && !k.IsRevoked
                && (!k.ExpiresAt.HasValue || k.ExpiresAt.Value > now));
        var storedHaUrl = await configStore.GetAsync("HomeAssistant:BaseUrl", ct).ConfigureAwait(false);
        var storedHaToken = await configStore.GetAsync("HomeAssistant:AccessToken", ct).ConfigureAwait(false);
        var hasHaConnection = !string.IsNullOrWhiteSpace(storedHaUrl) && !string.IsNullOrWhiteSpace(storedHaToken);

        if (hasDashboardKey && hasHaConnection)
        {
            var existingSigningKey = await configStore.GetAsync("Auth:SessionSigningKey", ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(existingSigningKey))
            {
                var signingKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
                await configStore.SetAsync("Auth:SessionSigningKey", signingKey, "env-seed", isSensitive: true, cancellationToken: ct).ConfigureAwait(false);
            }

            await configStore.SetAsync("Auth:SetupComplete", "true", "env-seed", cancellationToken: ct).ConfigureAwait(false);
            logger.LogInformation("Setup complete (all non-optional values present) — wizard skipped.");
        }
    }
}
