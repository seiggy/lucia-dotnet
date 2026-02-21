using System.Security.Claims;
using System.Security.Cryptography;
using FakeItEasy;
using lucia.AgentHost.Auth;
using lucia.Agents.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucia.Tests.Auth;

public class HmacSessionServiceTests
{
    private readonly HmacSessionService _service;

    public HmacSessionServiceTests()
    {
        var signingKey = RandomNumberGenerator.GetBytes(64);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:SessionSigningKey"] = Convert.ToBase64String(signingKey),
            })
            .Build();

        var options = Options.Create(new AuthOptions());
        var logger = A.Fake<ILogger<HmacSessionService>>();

        _service = new HmacSessionService(config, options, logger);
    }

    [Fact]
    public void CreateSession_ProducesSignedCookieWithTwoParts()
    {
        var cookie = _service.CreateSession("key-1", "Test Key");

        var parts = cookie.Split('.');
        Assert.Equal(2, parts.Length);
        Assert.False(string.IsNullOrWhiteSpace(parts[0]));
        Assert.False(string.IsNullOrWhiteSpace(parts[1]));
    }

    [Fact]
    public void ValidateSession_ReturnsClaimsForValidCookie()
    {
        var cookie = _service.CreateSession("key-1", "Test Key");

        var claims = _service.ValidateSession(cookie);

        Assert.NotNull(claims);
        var list = claims.ToList();
        Assert.Contains(list, c => c.Type == ClaimTypes.NameIdentifier && c.Value == "key-1");
        Assert.Contains(list, c => c.Type == ClaimTypes.Name && c.Value == "Test Key");
        Assert.Contains(list, c => c.Type == "auth_method" && c.Value == "session");
    }

    [Fact]
    public void ValidateSession_ReturnsNullForTamperedCookie()
    {
        var cookie = _service.CreateSession("key-1", "Test Key");
        var tampered = cookie + "x";

        var claims = _service.ValidateSession(tampered);

        Assert.Null(claims);
    }

    [Fact]
    public void ValidateSession_ReturnsNullForExpiredCookie()
    {
        // Use a negative lifetime so the token is already expired at creation time
        var signingKey = RandomNumberGenerator.GetBytes(64);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:SessionSigningKey"] = Convert.ToBase64String(signingKey),
            })
            .Build();

        var options = Options.Create(new AuthOptions
        {
            SessionLifetime = TimeSpan.FromSeconds(-5),
        });

        var shortLived = new HmacSessionService(config, options, A.Fake<ILogger<HmacSessionService>>());
        var cookie = shortLived.CreateSession("key-1", "Test Key");

        var claims = shortLived.ValidateSession(cookie);

        Assert.Null(claims);
    }

    [Fact]
    public void RoundTrip_CreateThenValidateReturnsOriginalClaims()
    {
        var keyId = "key-42";
        var keyName = "My Device";

        var cookie = _service.CreateSession(keyId, keyName);
        var claims = _service.ValidateSession(cookie);

        Assert.NotNull(claims);
        var list = claims.ToList();
        Assert.Equal(keyId, list.First(c => c.Type == ClaimTypes.NameIdentifier).Value);
        Assert.Equal(keyName, list.First(c => c.Type == ClaimTypes.Name).Value);
    }
}
