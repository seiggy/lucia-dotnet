using System.Security.Claims;
using lucia.Agents.Auth;
using lucia.Agents.Family;

namespace lucia.AgentHost.Auth;

/// <summary>
/// Turns a validated OIDC token into a member-only principal. Authority comes from the
/// administrator's identity link, never from claims the identity provider issued.
/// </summary>
public static class OidcMemberMapper
{
    public static async Task<bool> MapAsync(
        ClaimsPrincipal principal,
        MemberPrincipalResolver resolver,
        CancellationToken cancellationToken)
    {
        foreach (var identity in principal.Identities)
        {
            foreach (var claim in identity.Claims.Where(IsServerOwned).ToList())
            {
                identity.RemoveClaim(claim);
            }
        }

        var issuer = principal.FindFirstValue("iss");
        var subject = principal.FindFirstValue("sub");
        if (string.IsNullOrEmpty(issuer) || string.IsNullOrEmpty(subject)
            || principal.Identity is not ClaimsIdentity tokenIdentity)
        {
            return false;
        }

        var member = await resolver
            .ResolveAsync(MemberIdentityKind.Oidc, issuer, subject, cancellationToken)
            .ConfigureAwait(false);
        if (member is null)
        {
            return false;
        }

        var claims = new List<Claim> { new(LuciaClaims.Scope, ApiKeyScopes.Member) };
        MemberPrincipalResolver.AddMemberClaims(claims, member);
        tokenIdentity.AddClaims(claims);
        return true;
    }

    private static bool IsServerOwned(Claim claim) =>
        claim.Type.StartsWith("lucia:", StringComparison.Ordinal)
        || claim.Type == ClaimTypes.Role
        || claim.Type == "role"
        || claim.Type == "roles"
        || claim.Type == claim.Subject?.RoleClaimType;
}
