using System.Security.Claims;
using lucia.AgentHost.Auth;
using lucia.Agents.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace lucia.Tests.Auth;

public sealed class LuciaAuthorizationTests
{
    private static readonly IAuthorizationService Authorization = new ServiceCollection()
        .AddLogging()
        .AddAuthorization(LuciaAuthorization.ConfigurePolicies)
        .BuildServiceProvider()
        .GetRequiredService<IAuthorizationService>();

    private static ClaimsPrincipal Principal(string scheme, params Claim[] claims) =>
        new(new ClaimsIdentity(claims, scheme));

    private static readonly ClaimsPrincipal FullKey = Principal(
        AuthOptions.AuthenticationScheme,
        new Claim(LuciaClaims.Scope, ApiKeyScopes.Full));

    private static readonly ClaimsPrincipal MemberKey = Principal(
        AuthOptions.AuthenticationScheme,
        new Claim(LuciaClaims.Scope, ApiKeyScopes.Member),
        new Claim(LuciaClaims.MemberId, "kid"));

    private static readonly ClaimsPrincipal OidcMember = Principal(
        LuciaAuthorization.OidcScheme,
        new Claim(LuciaClaims.Scope, ApiKeyScopes.Member),
        new Claim(LuciaClaims.MemberId, "kid"));

    private static readonly ClaimsPrincipal InternalService = Principal(InternalTokenDefaults.AuthenticationScheme);

    private static async Task<bool> AllowsAsync(ClaimsPrincipal user, string policy) =>
        (await Authorization.AuthorizeAsync(user, policy)).Succeeded;

    [Fact]
    public async Task DefaultPolicy_AllowsFullAccessAndInternalButNotMembers()
    {
        var options = new AuthorizationOptions();
        LuciaAuthorization.ConfigurePolicies(options);

        Assert.True((await Authorization.AuthorizeAsync(FullKey, null, options.DefaultPolicy)).Succeeded);
        Assert.True((await Authorization.AuthorizeAsync(InternalService, null, options.DefaultPolicy)).Succeeded);
        Assert.False((await Authorization.AuthorizeAsync(MemberKey, null, options.DefaultPolicy)).Succeeded);
        Assert.False((await Authorization.AuthorizeAsync(OidcMember, null, options.DefaultPolicy)).Succeeded);
    }

    [Fact]
    public async Task ExternalOrInternal_RejectsMemberOnlyPrincipals()
    {
        Assert.True(await AllowsAsync(FullKey, LuciaAuthorization.ExternalOrInternalPolicy));
        Assert.True(await AllowsAsync(InternalService, LuciaAuthorization.ExternalOrInternalPolicy));
        Assert.False(await AllowsAsync(MemberKey, LuciaAuthorization.ExternalOrInternalPolicy));
    }

    [Fact]
    public async Task MemberPolicy_RequiresALinkedMember()
    {
        Assert.True(await AllowsAsync(MemberKey, LuciaAuthorization.MemberPolicy));
        Assert.True(await AllowsAsync(OidcMember, LuciaAuthorization.MemberPolicy));
        Assert.False(await AllowsAsync(FullKey, LuciaAuthorization.MemberPolicy));
        Assert.False(await AllowsAsync(InternalService, LuciaAuthorization.MemberPolicy));
    }

    [Fact]
    public async Task AdministratorPolicy_RequiresTheAdministratorRole()
    {
        var administrator = Principal(
            AuthOptions.AuthenticationScheme,
            new Claim(LuciaClaims.Scope, ApiKeyScopes.Full),
            new Claim(ClaimTypes.Role, AuthOptions.AdministratorRole));

        Assert.True(await AllowsAsync(administrator, AuthOptions.AdministratorPolicy));
        Assert.False(await AllowsAsync(FullKey, AuthOptions.AdministratorPolicy));
        Assert.False(await AllowsAsync(MemberKey, AuthOptions.AdministratorPolicy));
    }

    [Theory]
    [InlineData(null, false, AuthOptions.AuthenticationScheme)]
    [InlineData("Bearer internal-token", false, InternalTokenDefaults.AuthenticationScheme)]
    [InlineData("Bearer internal-token", true, InternalTokenDefaults.AuthenticationScheme)]
    [InlineData("Bearer aaa.bbb.ccc", false, InternalTokenDefaults.AuthenticationScheme)]
    [InlineData("Bearer aaa.bbb.ccc", true, LuciaAuthorization.OidcScheme)]
    public void SelectScheme_RoutesJwtsToOidcOnlyWhenEnabled(string? authorization, bool oidcEnabled, string expected)
    {
        var context = new DefaultHttpContext();
        if (authorization is not null)
        {
            context.Request.Headers.Authorization = authorization;
        }

        Assert.Equal(expected, LuciaAuthorization.SelectScheme(context, oidcEnabled));
    }
}
