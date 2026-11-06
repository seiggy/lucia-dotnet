using System.Security.Claims;
using lucia.Agents.Abstractions;
using lucia.Agents.Auth;
using lucia.Agents.Family;

namespace lucia.AgentHost.Auth;

/// <summary>
/// Resolves an authenticated identity to the family member it is linked to, if that member
/// may currently authenticate.
/// </summary>
public sealed class MemberPrincipalResolver(IFamilyMemberStore members, TimeProvider timeProvider)
{
    public async Task<FamilyMember?> ResolveAsync(
        MemberIdentityKind kind,
        string issuer,
        string subject,
        CancellationToken cancellationToken)
    {
        var member = await members
            .FindByIdentityAsync(kind, issuer, subject, cancellationToken)
            .ConfigureAwait(false);
        return member is not null && CanAuthenticate(member, timeProvider.GetUtcNow().UtcDateTime)
            ? member
            : null;
    }

    public static bool CanAuthenticate(FamilyMember member, DateTime utcNow) =>
        member.IsActive && (member.AccessExpiresAt is null || member.AccessExpiresAt > utcNow);

    public static void AddMemberClaims(ICollection<Claim> claims, FamilyMember member)
    {
        claims.Add(new Claim(LuciaClaims.MemberId, member.Id));
        claims.Add(new Claim(LuciaClaims.MemberRole, member.Role.ToString()));
    }
}
