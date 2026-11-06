using lucia.AgentHost.Auth;
using lucia.Agents.Abstractions;
using lucia.Agents.Auth;
using Microsoft.AspNetCore.Mvc;

namespace lucia.AgentHost.Apis;

/// <summary>
/// OIDC settings for family-member apps. The authentication scheme is built at startup, so a
/// saved change takes effect after the next restart.
/// </summary>
public static class OidcSettingsApi
{
    private const string Prefix = OidcAuthOptions.SectionName + ":";

    public static WebApplication MapOidcSettingsApi(this WebApplication app, bool requireAdministrator)
    {
        var group = app.MapGroup("/api/settings/oidc")
            .WithTags("Family Members");
        if (requireAdministrator)
        {
            group.RequireAuthorization(AuthOptions.AdministratorPolicy);
        }
        else
        {
            group.RequireAuthorization();
        }

        group.MapGet("/", GetAsync);
        group.MapPut("/", SaveAsync);
        return app;
    }

    private static async Task<IResult> GetAsync(
        IConfigStoreWriter configStore,
        OidcAuthOptions active,
        HttpContext httpContext)
    {
        var stored = await ReadAsync(configStore, httpContext.RequestAborted).ConfigureAwait(false);
        return Results.Ok(Describe(stored, active));
    }

    private static async Task<IResult> SaveAsync(
        [FromBody] OidcSettingsRequest request,
        IConfigStoreWriter configStore,
        OidcAuthOptions active,
        HttpContext httpContext)
    {
        var authority = request.Authority?.Trim();
        var audience = request.Audience?.Trim();
        if (request.Enabled)
        {
            if (!Uri.TryCreate(authority, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttps && (request.RequireHttpsMetadata || uri.Scheme != Uri.UriSchemeHttp)))
            {
                return Results.BadRequest(new { error = "The authority must be an absolute https URL." });
            }
            if (string.IsNullOrEmpty(audience))
            {
                return Results.BadRequest(new { error = "The audience is required." });
            }
        }

        var ct = httpContext.RequestAborted;
        const string updatedBy = "family-settings";
        await configStore.SetAsync(Prefix + nameof(OidcAuthOptions.Enabled), request.Enabled ? "true" : "false", updatedBy, cancellationToken: ct).ConfigureAwait(false);
        await configStore.SetAsync(Prefix + nameof(OidcAuthOptions.Authority), authority, updatedBy, cancellationToken: ct).ConfigureAwait(false);
        await configStore.SetAsync(Prefix + nameof(OidcAuthOptions.Audience), audience, updatedBy, cancellationToken: ct).ConfigureAwait(false);
        await configStore.SetAsync(Prefix + nameof(OidcAuthOptions.RequireHttpsMetadata), request.RequireHttpsMetadata ? "true" : "false", updatedBy, cancellationToken: ct).ConfigureAwait(false);

        var stored = await ReadAsync(configStore, ct).ConfigureAwait(false);
        return Results.Ok(Describe(stored, active));
    }

    private static async Task<OidcAuthOptions> ReadAsync(IConfigStoreWriter configStore, CancellationToken ct)
    {
        async Task<string?> Get(string name) =>
            await configStore.GetAsync(Prefix + name, ct).ConfigureAwait(false);

        return new OidcAuthOptions
        {
            Enabled = bool.TryParse(await Get(nameof(OidcAuthOptions.Enabled)).ConfigureAwait(false), out var enabled) && enabled,
            Authority = await Get(nameof(OidcAuthOptions.Authority)).ConfigureAwait(false),
            Audience = await Get(nameof(OidcAuthOptions.Audience)).ConfigureAwait(false),
            RequireHttpsMetadata = !bool.TryParse(await Get(nameof(OidcAuthOptions.RequireHttpsMetadata)).ConfigureAwait(false), out var https) || https,
        };
    }

    private static object Describe(OidcAuthOptions stored, OidcAuthOptions active) => new
    {
        enabled = stored.Enabled,
        authority = stored.Authority,
        audience = stored.Audience,
        requireHttpsMetadata = stored.RequireHttpsMetadata,
        active = active.IsConfigured,
        restartRequired = stored.IsConfigured != active.IsConfigured
            || (stored.IsConfigured
                && (stored.Authority != active.Authority
                    || stored.Audience != active.Audience
                    || stored.RequireHttpsMetadata != active.RequireHttpsMetadata)),
    };
}
