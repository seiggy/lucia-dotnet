using lucia.AgentHost.Auth;
using lucia.Agents.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace lucia.AgentHost.Extensions;

/// <summary>
/// Authentication endpoints: login, logout, session status.
/// </summary>
public static class AuthApi
{
    public static WebApplication MapAuthApi(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth")
            .WithTags("Authentication");

        group.MapPost("/login", LoginAsync).AllowAnonymous();
        group.MapPost("/logout", Logout).RequireAuthorization();
        group.MapGet("/status", GetStatus).AllowAnonymous();

        return app;
    }

    private static async Task<IResult> LoginAsync(
        [FromBody] LoginRequest request,
        IApiKeyService apiKeyService,
        ISessionService sessionService,
        IOptions<AuthOptions> authOptions,
        HttpContext httpContext)
    {
        if (string.IsNullOrWhiteSpace(request.ApiKey))
        {
            return Results.BadRequest(new { error = "API key is required." });
        }

        var entry = await apiKeyService.ValidateKeyAsync(request.ApiKey, httpContext.RequestAborted).ConfigureAwait(false);
        if (entry is null)
        {
            return Results.Unauthorized();
        }

        var options = authOptions.Value;
        var cookieValue = sessionService.CreateSession(entry.Id, entry.Name);

        httpContext.Response.Cookies.Append(options.CookieName, cookieValue, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Secure = httpContext.Request.IsHttps,
            MaxAge = options.SessionLifetime,
            Path = "/",
        });

        return Results.Ok(new
        {
            authenticated = true,
            keyName = entry.Name,
            keyPrefix = entry.KeyPrefix,
        });
    }

    private static IResult Logout(
        IOptions<AuthOptions> authOptions,
        HttpContext httpContext)
    {
        var options = authOptions.Value;
        httpContext.Response.Cookies.Delete(options.CookieName, new CookieOptions
        {
            Path = "/",
        });

        return Results.Ok(new { authenticated = false });
    }

    private static async Task<IResult> GetStatus(
        IApiKeyService apiKeyService,
        IConfiguration configuration,
        HttpContext httpContext)
    {
        var setupComplete = string.Equals(
            configuration["Auth:SetupComplete"],
            "true",
            StringComparison.OrdinalIgnoreCase);

        var authenticated = httpContext.User.Identity?.IsAuthenticated ?? false;
        var hasAnyKeys = await apiKeyService.HasAnyKeysAsync(httpContext.RequestAborted).ConfigureAwait(false);

        return Results.Ok(new
        {
            authenticated,
            setupComplete,
            hasKeys = hasAnyKeys,
        });
    }

    /// <summary>
    /// Login request body.
    /// </summary>
    public sealed record LoginRequest(string ApiKey);
}
