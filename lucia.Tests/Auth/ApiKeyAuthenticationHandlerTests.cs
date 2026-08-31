using System.Security.Claims;
using System.Text.Encodings.Web;

using FakeItEasy;
using lucia.AgentHost.Auth;
using lucia.Agents.Abstractions;
using lucia.Agents.Auth;
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
        A.CallTo(() => keys.ListKeysAsync(A<CancellationToken>._))
            .Returns([CreateKey(isRevoked: true)]);

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
        A.CallTo(() => keys.ListKeysAsync(A<CancellationToken>._))
            .Returns([CreateKey()]);

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
        A.CallTo(() => keys.ListKeysAsync(A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    private static async Task<AuthenticateResult> AuthenticateSessionAsync(
        IApiKeyService keys,
        IEnumerable<Claim> claims)
    {
        var sessions = A.Fake<ISessionService>();
        A.CallTo(() => sessions.ValidateSession("signed-session"))
            .Returns(claims);
        var schemeOptions =
            A.Fake<IOptionsMonitor<AuthenticationSchemeOptions>>();
        A.CallTo(() => schemeOptions.Get(A<string?>._))
            .Returns(new AuthenticationSchemeOptions());

        var authOptions = new AuthOptions();
        using var services = new ServiceCollection()
            .AddSingleton<IOptions<AuthOptions>>(Options.Create(authOptions))
            .BuildServiceProvider();
        var context = new DefaultHttpContext
        {
            RequestServices = services,
        };
        context.Request.Headers.Cookie =
            $"{authOptions.CookieName}=signed-session";
        var handler = new ApiKeyAuthenticationHandler(
            schemeOptions,
            NullLoggerFactory.Instance,
            UrlEncoder.Default,
            keys,
            sessions);
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
