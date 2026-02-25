using System.Security.Cryptography;
using System.Text.Json;
using lucia.Agents.Auth;
using lucia.Agents.Services;
using Microsoft.AspNetCore.Mvc;

namespace lucia.AgentHost.Extensions;

/// <summary>
/// Onboarding wizard endpoints.
///
/// Security model (three phases):
///   1. Pre-key: GET /status and POST /generate-dashboard-key are anonymous
///      because no credentials exist yet.
///   2. Post-key: all remaining endpoints require API key / session auth
///      so only the person who generated the key can proceed.
///   3. Post-complete: OnboardingMiddleware returns 403 for every /api/setup/*
///      request, permanently disabling the wizard. Users who lose their key
///      must reset MongoDB storage — there is no anonymous recovery path.
/// </summary>
public static class SetupApi
{
    public static WebApplication MapSetupApi(this WebApplication app)
    {
        var group = app.MapGroup("/api/setup")
            .WithTags("Setup Wizard");

        // Phase 1 — anonymous (pre-key): needed before any credentials exist
        group.MapGet("/status", GetSetupStatus).AllowAnonymous();
        group.MapPost("/generate-dashboard-key", GenerateDashboardKeyAsync).AllowAnonymous();

        // Phase 2 — authenticated: require the dashboard API key (header or session cookie)
        group.MapPost("/regenerate-dashboard-key", RegenerateDashboardKeyAsync).RequireAuthorization();
        group.MapPost("/configure-ha", ConfigureHomeAssistantAsync).RequireAuthorization();
        group.MapPost("/test-ha-connection", TestHaConnectionAsync).RequireAuthorization();
        group.MapPost("/generate-ha-key", GenerateHaKeyAsync).RequireAuthorization();
        group.MapPost("/validate-ha-connection", ValidateHaConnectionAsync).RequireAuthorization();
        group.MapGet("/ha-status", GetHaStatusAsync).RequireAuthorization();
        group.MapPost("/complete", CompleteSetupAsync).RequireAuthorization();

        return app;
    }

    /// <summary>
    /// Returns setup wizard state — which steps have been completed.
    /// </summary>
    private static async Task<IResult> GetSetupStatus(
        IApiKeyService apiKeyService,
        ConfigStoreWriter configStore,
        HttpContext httpContext)
    {
        var ct = httpContext.RequestAborted;

        var hasKeys = await apiKeyService.HasAnyKeysAsync(ct).ConfigureAwait(false);
        var haUrl = await configStore.GetAsync("HomeAssistant:BaseUrl", ct).ConfigureAwait(false);
        var haToken = await configStore.GetAsync("HomeAssistant:AccessToken", ct).ConfigureAwait(false);
        var pluginValidated = await configStore.GetAsync("HomeAssistant:PluginValidated", ct).ConfigureAwait(false);
        var setupComplete = await configStore.GetAsync("Auth:SetupComplete", ct).ConfigureAwait(false);

        return Results.Ok(new
        {
            hasDashboardKey = hasKeys,
            hasHaConnection = !string.IsNullOrWhiteSpace(haUrl) && !string.IsNullOrWhiteSpace(haToken),
            haUrl = !string.IsNullOrWhiteSpace(haUrl) ? haUrl : null,
            pluginValidated = string.Equals(pluginValidated, "true", StringComparison.OrdinalIgnoreCase),
            setupComplete = string.Equals(setupComplete, "true", StringComparison.OrdinalIgnoreCase),
        });
    }

    /// <summary>
    /// Step 2a: Generate the Dashboard API key. Returns the plaintext key once.
    /// </summary>
    private static async Task<IResult> GenerateDashboardKeyAsync(
        IApiKeyService apiKeyService,
        HttpContext httpContext)
    {
        // Idempotent: if a Dashboard key already exists and is active, don't create another
        var existingKeys = await apiKeyService.ListKeysAsync(httpContext.RequestAborted).ConfigureAwait(false);
        var dashboardKey = existingKeys.FirstOrDefault(k => k.Name == "Dashboard" && !k.IsRevoked);
        if (dashboardKey is not null)
        {
            return Results.Conflict(new
            {
                error = "Dashboard key already exists. Use the key management API to regenerate it.",
                keyPrefix = dashboardKey.KeyPrefix,
            });
        }

        var result = await apiKeyService.CreateKeyAsync("Dashboard", httpContext.RequestAborted).ConfigureAwait(false);

        return Results.Ok(new
        {
            key = result.Key,
            prefix = result.Prefix,
            message = "Save this API key now — it cannot be shown again. You will use it to log into the Lucia dashboard.",
        });
    }

    /// <summary>
    /// Regenerates the Dashboard API key during setup. Revokes the old one and creates a new one.
    /// Only works before setup is complete.
    /// </summary>
    private static async Task<IResult> RegenerateDashboardKeyAsync(
        IApiKeyService apiKeyService,
        HttpContext httpContext)
    {
        var keys = await apiKeyService.ListKeysAsync(httpContext.RequestAborted).ConfigureAwait(false);
        var existing = keys.FirstOrDefault(k => k.Name == "Dashboard" && !k.IsRevoked);

        if (existing is null)
        {
            // No existing key — just create one
            var created = await apiKeyService.CreateKeyAsync("Dashboard", httpContext.RequestAborted).ConfigureAwait(false);
            return Results.Ok(new
            {
                key = created.Key,
                prefix = created.Prefix,
                message = "Save this API key now — it cannot be shown again.",
            });
        }

        var result = await apiKeyService.RegenerateKeyAsync(existing.Id, httpContext.RequestAborted).ConfigureAwait(false);
        return Results.Ok(new
        {
            key = result.Key,
            prefix = result.Prefix,
            message = "New key generated. The previous Dashboard key has been revoked.",
        });
    }

    /// <summary>
    /// Step 2b: Configure the Home Assistant connection (URL + long-lived access token).
    /// </summary>
    private static async Task<IResult> ConfigureHomeAssistantAsync(
        [FromBody] ConfigureHaRequest request,
        ConfigStoreWriter configStore,
        HttpContext httpContext)
    {
        if (string.IsNullOrWhiteSpace(request.BaseUrl))
        {
            return Results.BadRequest(new { error = "Home Assistant URL is required." });
        }

        if (!Uri.TryCreate(request.BaseUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            return Results.BadRequest(new { error = "Invalid Home Assistant URL. Must be http:// or https://." });
        }

        if (string.IsNullOrWhiteSpace(request.AccessToken))
        {
            return Results.BadRequest(new { error = "Home Assistant long-lived access token is required." });
        }

        var ct = httpContext.RequestAborted;

        await configStore.SetAsync("HomeAssistant:BaseUrl", request.BaseUrl.TrimEnd('/'), "setup-wizard", cancellationToken: ct).ConfigureAwait(false);
        await configStore.SetAsync("HomeAssistant:AccessToken", request.AccessToken, "setup-wizard", isSensitive: true, cancellationToken: ct).ConfigureAwait(false);

        return Results.Ok(new { saved = true });
    }

    /// <summary>
    /// Step 2c: Test the Lucia → Home Assistant connection using the saved URL and token.
    /// Creates a temporary HttpClient since the DI-registered client may not have the
    /// newly-configured URL yet (first-run setup).
    /// </summary>
    private static async Task<IResult> TestHaConnectionAsync(
        ConfigStoreWriter configStore,
        IHttpClientFactory httpClientFactory,
        IEntityLocationService entityLocationService,
        HttpContext httpContext)
    {
        var connected = false;

        try
        {
            var ct = httpContext.RequestAborted;
            var baseUrl = await configStore.GetAsync("HomeAssistant:BaseUrl", ct).ConfigureAwait(false);
            var token = await configStore.GetAsync("HomeAssistant:AccessToken", ct).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(token))
            {
                return Results.Ok(new
                {
                    connected = false,
                    error = "Home Assistant URL or access token not configured yet.",
                });
            }

            using var client = httpClientFactory.CreateClient();
            client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            client.DefaultRequestHeaders.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

            // Test with /api/ root endpoint
            var response = await client.GetAsync("api/", ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            // Try to get config for version/location info
            string? haVersion = null;
            string? locationName = null;
            try
            {
                var configResponse = await client.GetAsync("api/config", ct).ConfigureAwait(false);
                if (configResponse.IsSuccessStatusCode)
                {
                    var body = await configResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    var json = JsonDocument.Parse(body).RootElement;
                    haVersion = json.TryGetProperty("version", out var v) ? v.GetString() : null;
                    locationName = json.TryGetProperty("location_name", out var l) ? l.GetString() : null;
                }
            }
            catch
            {
                // Config fetch is best-effort
            }

            connected = true;

            return Results.Ok(new
            {
                connected = true,
                message = "Connected to Home Assistant",
                haVersion,
                locationName,
            });
        }
        catch (HttpRequestException ex)
        {
            return Results.Ok(new
            {
                connected = false,
                error = $"Failed to connect to Home Assistant: {ex.Message}",
            });
        }
        catch (Exception ex)
        {
            return Results.Ok(new
            {
                connected = false,
                error = $"Unexpected error: {ex.Message}",
            });
        }
        finally
        {
            // Pre-populate the entity location cache now that HA is confirmed reachable
            if (connected)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await entityLocationService.InvalidateAndReloadAsync().ConfigureAwait(false);
                    }
                    catch
                    {
                        // Best-effort — the cache will self-heal on next access if this fails
                    }
                });
            }
        }
    }

    /// <summary>
    /// Step 3a: Generate the Home Assistant API key for the HA plugin to use when calling Lucia.
    /// </summary>
    private static async Task<IResult> GenerateHaKeyAsync(
        IApiKeyService apiKeyService,
        HttpContext httpContext)
    {
        var existingKeys = await apiKeyService.ListKeysAsync(httpContext.RequestAborted).ConfigureAwait(false);
        var haKey = existingKeys.FirstOrDefault(k => k.Name == "Home Assistant" && !k.IsRevoked);
        if (haKey is not null)
        {
            return Results.Conflict(new
            {
                error = "Home Assistant key already exists. Use the key management API to regenerate it.",
                keyPrefix = haKey.KeyPrefix,
            });
        }

        var result = await apiKeyService.CreateKeyAsync("Home Assistant", httpContext.RequestAborted).ConfigureAwait(false);

        return Results.Ok(new
        {
            key = result.Key,
            prefix = result.Prefix,
            message = "Enter this API key in the Lucia custom component configuration in Home Assistant.",
        });
    }

    /// <summary>
    /// Step 3b: Called by the HA custom component to validate connectivity back to Lucia.
    /// Requires auth — the HA plugin sends the API key it was configured with,
    /// validated by the ASP.NET Core auth middleware (RequireAuthorization).
    /// </summary>
    private static async Task<IResult> ValidateHaConnectionAsync(
        [FromBody] ValidateHaConnectionRequest request,
        ConfigStoreWriter configStore,
        HttpContext httpContext)
    {
        if (string.IsNullOrWhiteSpace(request.HomeAssistantInstanceId))
        {
            return Results.BadRequest(new { error = "Home Assistant instance ID is required." });
        }

        var ct = httpContext.RequestAborted;

        await configStore.SetAsync("HomeAssistant:PluginValidated", "true", "ha-plugin", cancellationToken: ct).ConfigureAwait(false);
        await configStore.SetAsync("HomeAssistant:InstanceId", request.HomeAssistantInstanceId, "ha-plugin", cancellationToken: ct).ConfigureAwait(false);
        await configStore.SetAsync("HomeAssistant:LastValidatedAt", DateTime.UtcNow.ToString("O"), "ha-plugin", cancellationToken: ct).ConfigureAwait(false);

        return Results.Ok(new
        {
            valid = true,
            instanceId = request.HomeAssistantInstanceId,
            timestamp = DateTime.UtcNow,
        });
    }

    /// <summary>
    /// Step 3c: Polled by the setup wizard to check if the HA plugin has called back.
    /// </summary>
    private static async Task<IResult> GetHaStatusAsync(
        ConfigStoreWriter configStore,
        HttpContext httpContext)
    {
        var ct = httpContext.RequestAborted;
        var validated = await configStore.GetAsync("HomeAssistant:PluginValidated", ct).ConfigureAwait(false);
        var instanceId = await configStore.GetAsync("HomeAssistant:InstanceId", ct).ConfigureAwait(false);
        var lastValidated = await configStore.GetAsync("HomeAssistant:LastValidatedAt", ct).ConfigureAwait(false);

        return Results.Ok(new
        {
            pluginConnected = string.Equals(validated, "true", StringComparison.OrdinalIgnoreCase),
            instanceId,
            lastValidatedAt = lastValidated,
        });
    }

    /// <summary>
    /// Step 4: Complete setup — mark as done, generate session signing key.
    /// </summary>
    private static async Task<IResult> CompleteSetupAsync(
        IApiKeyService apiKeyService,
        ConfigStoreWriter configStore,
        HttpContext httpContext)
    {
        var ct = httpContext.RequestAborted;

        // Verify at least one key exists
        var hasKeys = await apiKeyService.HasAnyKeysAsync(ct).ConfigureAwait(false);
        if (!hasKeys)
        {
            return Results.BadRequest(new { error = "Cannot complete setup without generating at least one API key." });
        }

        // Generate and store session signing key if not already present
        var existingSigningKey = await configStore.GetAsync("Auth:SessionSigningKey", ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(existingSigningKey))
        {
            var signingKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
            await configStore.SetAsync("Auth:SessionSigningKey", signingKey, "setup-wizard", isSensitive: true, cancellationToken: ct).ConfigureAwait(false);
        }

        // Mark setup as complete
        await configStore.SetAsync("Auth:SetupComplete", "true", "setup-wizard", cancellationToken: ct).ConfigureAwait(false);

        return Results.Ok(new
        {
            setupComplete = true,
            message = "Setup complete! You can now log in with your Dashboard API key.",
        });
    }

    public sealed record ConfigureHaRequest(string BaseUrl, string AccessToken);
    public sealed record ValidateHaConnectionRequest(string HomeAssistantInstanceId);
}
