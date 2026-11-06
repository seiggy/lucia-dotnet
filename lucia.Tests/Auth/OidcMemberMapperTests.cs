using System.Security.Claims;
using lucia.AgentHost.Auth;
using lucia.Agents.Abstractions;
using lucia.Agents.Auth;
using lucia.Agents.Family;
using FakeItEasy;

namespace lucia.Tests.Auth;

public sealed class OidcMemberMapperTests
{
    private static ClaimsPrincipal Token(string? issuer = "https://id.example", string? subject = "sub-1")
    {
        var claims = new List<Claim>();
        if (issuer is not null)
        {
            claims.Add(new Claim("iss", issuer));
        }
        if (subject is not null)
        {
            claims.Add(new Claim("sub", subject));
        }
        return new ClaimsPrincipal(new ClaimsIdentity(claims, LuciaAuthorization.OidcScheme));
    }

    private static MemberPrincipalResolver Resolver(FamilyMember? member)
    {
        var members = A.Fake<IFamilyMemberStore>();
        A.CallTo(() => members.FindByIdentityAsync(A<MemberIdentityKind>._, A<string>._, A<string>._, A<CancellationToken>._))
            .Returns((FamilyMember?)null);
        if (member is not null)
        {
            A.CallTo(() => members.FindByIdentityAsync(MemberIdentityKind.Oidc, "https://id.example", "sub-1", A<CancellationToken>._))
                .Returns(member);
        }
        return new MemberPrincipalResolver(members, TimeProvider.System);
    }

    [Fact]
    public async Task MapAsync_LinkedMember_AddsMemberClaimsAndOnlyMemberScope()
    {
        var principal = Token();
        var member = new FamilyMember { Id = "kid", DisplayName = "Sam", Role = FamilyRole.Child };

        Assert.True(await OidcMemberMapper.MapAsync(principal, Resolver(member), CancellationToken.None));

        Assert.Equal("kid", principal.FindFirstValue(LuciaClaims.MemberId));
        Assert.True(principal.HasClaim(LuciaClaims.Scope, ApiKeyScopes.Member));
        Assert.False(LuciaAuthorization.HasFullAccess(principal));
    }

    [Theory]
    [InlineData("https://id.example", "unknown")]
    [InlineData("https://other.example", "sub-1")]
    [InlineData(null, "sub-1")]
    [InlineData("https://id.example", null)]
    public async Task MapAsync_UnlinkedOrIncompleteToken_IsRejected(string? issuer, string? subject)
    {
        var member = new FamilyMember { Id = "kid", DisplayName = "Sam", Role = FamilyRole.Child };

        Assert.False(await OidcMemberMapper.MapAsync(Token(issuer, subject), Resolver(member), CancellationToken.None));
    }

    [Fact]
    public async Task MapAsync_ClientSuppliedLuciaClaims_AreNotTrusted()
    {
        var principal = Token();
        ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim(LuciaClaims.Scope, ApiKeyScopes.Full));

        Assert.False(await OidcMemberMapper.MapAsync(principal, Resolver(null), CancellationToken.None));
        Assert.False(LuciaAuthorization.HasFullAccess(principal));
    }

    [Fact]
    public async Task MapAsync_TokenRolesAndLuciaClaims_AreReplacedByServerClaims()
    {
        var principal = Token();
        var identity = (ClaimsIdentity)principal.Identity!;
        identity.AddClaim(new Claim(ClaimTypes.Role, AuthOptions.AdministratorRole));
        identity.AddClaim(new Claim("role", AuthOptions.AdministratorRole));
        identity.AddClaim(new Claim(LuciaClaims.Scope, ApiKeyScopes.Full));
        identity.AddClaim(new Claim(LuciaClaims.MemberId, "owner"));
        var member = new FamilyMember { Id = "kid", DisplayName = "Sam", Role = FamilyRole.Child };

        Assert.True(await OidcMemberMapper.MapAsync(principal, Resolver(member), CancellationToken.None));

        Assert.False(principal.IsInRole(AuthOptions.AdministratorRole));
        Assert.False(principal.HasClaim("role", AuthOptions.AdministratorRole));
        Assert.False(LuciaAuthorization.HasFullAccess(principal));
        Assert.Equal(["kid"], principal.FindAll(LuciaClaims.MemberId).Select(c => c.Value));
    }
}
