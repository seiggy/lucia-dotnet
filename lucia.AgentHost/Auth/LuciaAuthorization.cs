using System.Security.Claims;
using lucia.Agents.Auth;
using Microsoft.AspNetCore.Authorization;

namespace lucia.AgentHost.Auth;

/// <summary>
/// Authorization policies and scheme routing. Member-only principals (device keys and OIDC
/// members) reach only endpoints that opt into <see cref="MemberPolicy"/>.
/// </summary>
public static class LuciaAuthorization
{
    public const string OidcScheme = "Oidc";

    public const string MemberPolicy = "Member";

    public const string InternalOnlyPolicy = "InternalOnly";

    public const string ExternalOrInternalPolicy = "ExternalOrInternal";

    public static bool HasFullAccess(ClaimsPrincipal user) =>
        user.Identities.Any(identity =>
            identity.IsAuthenticated
            && identity.AuthenticationType == InternalTokenDefaults.AuthenticationScheme)
        || user.HasClaim(LuciaClaims.Scope, ApiKeyScopes.Full);

    public static void ConfigurePolicies(AuthorizationOptions options)
    {
        options.DefaultPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireAssertion(context => HasFullAccess(context.User))
            .Build();

        options.AddPolicy(
            AuthOptions.AdministratorPolicy,
            policy => policy.RequireRole(AuthOptions.AdministratorRole));

        // Internal-only: only platform-injected token (agent → registry)
        options.AddPolicy(InternalOnlyPolicy, policy =>
            policy.AddAuthenticationSchemes(InternalTokenDefaults.AuthenticationScheme)
                  .RequireAuthenticatedUser());

        // External or internal: either a full-access API key/session OR the internal token
        options.AddPolicy(ExternalOrInternalPolicy, policy =>
            policy.AddAuthenticationSchemes(
                    AuthOptions.AuthenticationScheme,
                    InternalTokenDefaults.AuthenticationScheme)
                  .RequireAuthenticatedUser()
                  .RequireAssertion(context => HasFullAccess(context.User)));

        options.AddPolicy(MemberPolicy, policy =>
            policy.RequireAuthenticatedUser()
                  .RequireClaim(LuciaClaims.MemberId));
    }

    /// <summary>
    /// Bearer JWTs go to OIDC when it is enabled; other bearer tokens are the internal token.
    /// Everything else is an API key or session cookie.
    /// </summary>
    public static string SelectScheme(HttpContext context, bool oidcEnabled)
    {
        var authorization = context.Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return AuthOptions.AuthenticationScheme;
        }

        var token = authorization.AsSpan("Bearer ".Length).Trim();
        return oidcEnabled && token.Count('.') == 2
            ? OidcScheme
            : InternalTokenDefaults.AuthenticationScheme;
    }
}
