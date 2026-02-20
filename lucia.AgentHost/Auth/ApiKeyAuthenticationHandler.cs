using System.Security.Claims;
using System.Text.Encodings.Web;
using lucia.Agents.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace lucia.AgentHost.Auth;

/// <summary>
/// ASP.NET Core authentication handler that validates API keys from the X-API-Key header
/// or session cookies. API key header takes precedence when both are present.
/// </summary>
public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly IApiKeyService _apiKeyService;
    private readonly ISessionService _sessionService;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IApiKeyService apiKeyService,
        ISessionService sessionService)
        : base(options, logger, encoder)
    {
        _apiKeyService = apiKeyService;
        _sessionService = sessionService;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // 1. Check X-API-Key header first (explicit > implicit)
        if (Request.Headers.TryGetValue(AuthOptions.ApiKeyHeaderName, out var apiKeyHeader))
        {
            var apiKey = apiKeyHeader.ToString();
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                return await ValidateApiKeyAsync(apiKey).ConfigureAwait(false);
            }
        }

        // 2. Check session cookie
        var authOptions = Context.RequestServices.GetService<IOptions<AuthOptions>>()?.Value
            ?? new AuthOptions();

        if (Request.Cookies.TryGetValue(authOptions.CookieName, out var cookieValue)
            && !string.IsNullOrWhiteSpace(cookieValue))
        {
            return ValidateSessionCookie(cookieValue);
        }

        return AuthenticateResult.NoResult();
    }

    private async Task<AuthenticateResult> ValidateApiKeyAsync(string apiKey)
    {
        var entry = await _apiKeyService.ValidateKeyAsync(apiKey, Context.RequestAborted).ConfigureAwait(false);
        if (entry is null)
        {
            return AuthenticateResult.Fail("Invalid or revoked API key.");
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, entry.Id),
            new Claim(ClaimTypes.Name, entry.Name),
            new Claim("auth_method", "api_key"),
            new Claim("key_prefix", entry.KeyPrefix),
        };

        var identity = new ClaimsIdentity(claims, AuthOptions.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, AuthOptions.AuthenticationScheme);

        return AuthenticateResult.Success(ticket);
    }

    private AuthenticateResult ValidateSessionCookie(string cookieValue)
    {
        var claims = _sessionService.ValidateSession(cookieValue);
        if (claims is null)
        {
            return AuthenticateResult.Fail("Invalid or expired session.");
        }

        var identity = new ClaimsIdentity(claims, AuthOptions.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, AuthOptions.AuthenticationScheme);

        return AuthenticateResult.Success(ticket);
    }
}
