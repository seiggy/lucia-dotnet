using lucia.AgentHost.Family;
using lucia.Agents.Abstractions;
using lucia.Agents.Auth;
using lucia.Agents.Family;
using Microsoft.AspNetCore.Mvc;

namespace lucia.AgentHost.Apis;

/// <summary>
/// Device pairing: administrators issue one-time codes (shown as a QR code); a member's app
/// redeems a code for its own member-only device key.
/// </summary>
public static class PairingApi
{
    public const string RedeemRateLimitPolicy = "pairing-redeem";

    public static WebApplication MapPairingApi(this WebApplication app, bool requireAdministrator)
    {
        var admin = app.MapGroup("/api/family")
            .WithTags("Family Pairing");
        if (requireAdministrator)
        {
            admin.RequireAuthorization(AuthOptions.AdministratorPolicy);
        }
        else
        {
            admin.RequireAuthorization();
        }

        admin.MapPost("/members/{memberId}/pairings", CreateAsync);
        admin.MapGet("/members/{memberId}/pairings", ListAsync);
        admin.MapDelete("/pairings/{pairingId}", DeleteAsync);

        app.MapPost("/api/pairing/redeem", RedeemAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RedeemRateLimitPolicy)
            .WithTags("Family Pairing");

        return app;
    }

    private static async Task<IResult> CreateAsync(
        string memberId,
        [FromBody] CreatePairingRequest? request,
        DevicePairingService pairing,
        IConfiguration configuration,
        HttpContext httpContext)
    {
        var lifetime = request?.LifetimeMinutes is > 0 and var minutes
            ? TimeSpan.FromMinutes(minutes)
            : (TimeSpan?)null;
        var created = await pairing.CreateAsync(memberId, lifetime, httpContext.RequestAborted).ConfigureAwait(false);
        if (created is null)
        {
            return Results.NotFound(new { error = "Family member not found or not active." });
        }

        var serverUrl = configuration["Auth:PublicBaseUrl"]
            ?? $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
        return Results.Ok(new
        {
            id = created.Pairing.Id,
            memberId = created.Pairing.MemberId,
            code = created.Code,
            expiresAt = created.Pairing.ExpiresAt,
            serverUrl,
            pairingUri = $"lucia://pair?server={Uri.EscapeDataString(serverUrl)}&code={Uri.EscapeDataString(created.Code)}",
        });
    }

    private static async Task<IResult> ListAsync(
        string memberId,
        IDevicePairingStore pairings,
        HttpContext httpContext)
    {
        var list = await pairings.ListForMemberAsync(memberId, httpContext.RequestAborted).ConfigureAwait(false);
        return Results.Ok(list.Select(Summarize));
    }

    private static async Task<IResult> DeleteAsync(
        string pairingId,
        IDevicePairingStore pairings,
        HttpContext httpContext)
    {
        return await pairings.DeleteAsync(pairingId, httpContext.RequestAborted).ConfigureAwait(false)
            ? Results.NoContent()
            : Results.NotFound();
    }

    private static async Task<IResult> RedeemAsync(
        [FromBody] RedeemPairingRequest request,
        DevicePairingService pairing,
        HttpContext httpContext)
    {
        var redeemed = await pairing
            .RedeemAsync(request.Code, request.DeviceName, httpContext.RequestAborted)
            .ConfigureAwait(false);
        if (redeemed is null)
        {
            return Results.BadRequest(new { error = "The pairing code is invalid, expired, or already used." });
        }

        return Results.Ok(new
        {
            apiKey = redeemed.ApiKey,
            keyId = redeemed.KeyId,
            member = new
            {
                id = redeemed.Member.Id,
                displayName = redeemed.Member.DisplayName,
                role = redeemed.Member.Role,
            },
        });
    }

    private static object Summarize(DevicePairing pairing) => new
    {
        id = pairing.Id,
        memberId = pairing.MemberId,
        deviceName = pairing.DeviceName,
        createdAt = pairing.CreatedAt,
        expiresAt = pairing.ExpiresAt,
        redeemedAt = pairing.RedeemedAt,
        apiKeyId = pairing.ApiKeyId,
    };
}
