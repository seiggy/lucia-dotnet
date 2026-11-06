using System.Security.Claims;
using System.Text.Encodings.Web;

using FakeItEasy;
using lucia.AgentHost.Auth;
using lucia.Agents.Abstractions;
using lucia.Agents.Auth;
using lucia.Agents.Family;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace lucia.Tests.Auth;

public sealed class ApiKeyAuthenticationHandlerTests
{
    [Fact]
    public async Task AdministratorSession_RevokedKey_FailsAuthentication()
    {
        var keys = A.Fake<IApiKeyService>();
        A.CallTo(() => keys.GetKeyAsync(
                A<string>._,
                A<CancellationToken>._))
            .Returns(CreateKey(isRevoked: true));

        var result = await AuthenticateSessionAsync(
            keys,
            CreateAdministratorClaims());

        Assert.False(result.Succeeded);
        Assert.Contains("revoked", result.Failure?.Message);
    }

    [Fact]
    public async Task AdministratorSession_ActiveAdministratorKey_Succeeds()
    {
        var keys = A.Fake<IApiKeyService>();
        A.CallTo(() => keys.GetKeyAsync(
                A<string>._,
                A<CancellationToken>._))
            .Returns(CreateKey());

        var result = await AuthenticateSessionAsync(
            keys,
            CreateAdministratorClaims());

        Assert.True(result.Succeeded);
        Assert.True(result.Principal?.IsInRole(AuthOptions.AdministratorRole));
    }

    [Fact]
    public async Task OrdinarySession_DoesNotQueryCurrentKeyState()
    {
        var keys = A.Fake<IApiKeyService>();

        var result = await AuthenticateSessionAsync(
            keys,
            [
                new Claim(ClaimTypes.NameIdentifier, "key-1"),
                new Claim(ClaimTypes.Name, "Dashboard"),
                new Claim("auth_method", "session"),
            ]);

        Assert.True(result.Succeeded);
        Assert.True(result.Principal!.HasClaim(LuciaClaims.Scope, ApiKeyScopes.Full));
        A.CallTo(() => keys.GetKeyAsync(
                A<string>._,
                A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task MemberKey_LinkedToActiveMember_AddsMemberClaimsWithoutFullAccess()
    {
        var members = MembersWith(new FamilyMember { Id = "kid", DisplayName = "Kid", Role = FamilyRole.Child });

        var result = await AuthenticateKeyAsync(members, [ApiKeyScopes.Member]);

        Assert.True(result.Succeeded);
        var principal = result.Principal!;
        Assert.Equal("kid", principal.FindFirstValue(LuciaClaims.MemberId));
        Assert.Equal(nameof(FamilyRole.Child), principal.FindFirstValue(LuciaClaims.MemberRole));
        Assert.True(principal.HasClaim(LuciaClaims.Scope, ApiKeyScopes.Member));
        Assert.False(principal.HasClaim(LuciaClaims.Scope, ApiKeyScopes.Full));
        Assert.False(principal.IsInRole(AuthOptions.AdministratorRole));
    }

    [Theory]
    [InlineData("unlinked")]
    [InlineData("inactive")]
    [InlineData("expired")]
    public async Task MemberKey_WithoutAnActiveMember_Fails(string setup)
    {
        var member = new FamilyMember { Id = "guest", DisplayName = "Guest", Role = FamilyRole.Guest };
        if (setup == "inactive")
        {
            member.IsActive = false;
        }
        else if (setup == "expired")
        {
            member.AccessExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        }
        var members = setup == "unlinked" ? NoMembers() : MembersWith(member);

        var result = await AuthenticateKeyAsync(members, [ApiKeyScopes.Member]);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task FullKey_LinkedToMember_KeepsFullAccessAndAddsMemberClaims()
    {
        var members = MembersWith(new FamilyMember { Id = "owner", DisplayName = "Owner", Role = FamilyRole.Administrator });

        var result = await AuthenticateKeyAsync(members, [ApiKeyScopes.Full]);

        Assert.True(result.Succeeded);
        Assert.True(result.Principal!.HasClaim(LuciaClaims.Scope, ApiKeyScopes.Full));
        Assert.Equal("owner", result.Principal.FindFirstValue(LuciaClaims.MemberId));
    }

    [Fact]
    public async Task FullKey_WithoutMember_SucceedsAsBefore()
    {
        var result = await AuthenticateKeyAsync(NoMembers(), [ApiKeyScopes.Full]);

        Assert.True(result.Succeeded);
        Assert.Null(result.Principal!.FindFirstValue(LuciaClaims.MemberId));
    }

    private static IFamilyMemberStore NoMembers()
    {
        var members = A.Fake<IFamilyMemberStore>();
        A.CallTo(() => members.FindByIdentityAsync(A<MemberIdentityKind>._, A<string>._, A<string>._, A<CancellationToken>._))
            .Returns((FamilyMember?)null);
        return members;
    }

    private static IFamilyMemberStore MembersWith(FamilyMember member)
    {
        var members = A.Fake<IFamilyMemberStore>();
        A.CallTo(() => members.FindByIdentityAsync(MemberIdentityKind.ApiKey, string.Empty, "key-1", A<CancellationToken>._))
            .Returns(member);
        return members;
    }

    private static async Task<AuthenticateResult> AuthenticateKeyAsync(
        IFamilyMemberStore members,
        string[] scopes)
    {
        var keys = A.Fake<IApiKeyService>();
        A.CallTo(() => keys.ValidateKeyAsync("lk_device", A<CancellationToken>._))
            .Returns(new ApiKeyEntry
            {
                Id = "key-1",
                KeyHash = "hash",
                KeyPrefix = "lk_device...",
                Name = "Phone",
                Scopes = scopes,
            });
        var context = new DefaultHttpContext();
        context.Request.Headers[AuthOptions.ApiKeyHeaderName] = "lk_device";
        return await AuthenticateAsync(keys, A.Fake<ISessionService>(), members, context);
    }

    private static async Task<AuthenticateResult> AuthenticateSessionAsync(
        IApiKeyService keys,
        IEnumerable<Claim> claims)
    {
        var sessions = A.Fake<ISessionService>();
        A.CallTo(() => sessions.ValidateSession("signed-session"))
            .Returns(claims);
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie =
            $"{new AuthOptions().CookieName}=signed-session";
        return await AuthenticateAsync(keys, sessions, NoMembers(), context);
    }

    private static async Task<AuthenticateResult> AuthenticateAsync(
        IApiKeyService keys,
        ISessionService sessions,
        IFamilyMemberStore members,
        DefaultHttpContext context)
    {
        var schemeOptions =
            A.Fake<IOptionsMonitor<AuthenticationSchemeOptions>>();
        A.CallTo(() => schemeOptions.Get(A<string?>._))
            .Returns(new AuthenticationSchemeOptions());

        using var services = new ServiceCollection()
            .AddSingleton<IOptions<AuthOptions>>(Options.Create(new AuthOptions()))
            .BuildServiceProvider();
        context.RequestServices = services;
        var handler = new ApiKeyAuthenticationHandler(
            schemeOptions,
            NullLoggerFactory.Instance,
            UrlEncoder.Default,
            keys,
            sessions,
            new MemberPrincipalResolver(members, TimeProvider.System));
        var scheme = new AuthenticationScheme(
            AuthOptions.AuthenticationScheme,
            AuthOptions.AuthenticationScheme,
            typeof(ApiKeyAuthenticationHandler));
        await handler.InitializeAsync(scheme, context);

        return await handler.AuthenticateAsync();
    }

    private static Claim[] CreateAdministratorClaims() =>
    [
        new Claim(ClaimTypes.NameIdentifier, "key-1"),
        new Claim(ClaimTypes.Name, "Dashboard"),
        new Claim("auth_method", "session"),
        new Claim(ClaimTypes.Role, AuthOptions.AdministratorRole),
    ];

    private static ApiKeySummary CreateKey(bool isRevoked = false) =>
        new()
        {
            Id = "key-1",
            KeyPrefix = "lk_owner...",
            Name = "Dashboard",
            CreatedAt = DateTime.UtcNow,
            LastUsedAt = null,
            ExpiresAt = null,
            IsRevoked = isRevoked,
            RevokedAt = isRevoked ? DateTime.UtcNow : null,
            Scopes = ["*", AuthOptions.AdministratorScope],
        };
}
